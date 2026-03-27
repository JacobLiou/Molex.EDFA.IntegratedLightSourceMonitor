#include "stdafx.h"
#include "PDArry.h"
#include "afxdialogex.h"
#ifdef _DEBUG
#undef THIS_FILE
static char THIS_FILE[] = __FILE__;
#define new DEBUG_NEW
#endif

//////////////////////////////////////////////////////////////////////
// Construction/Destruction
//////////////////////////////////////////////////////////////////////
#pragma comment(lib, "libusbK.lib" )

CPDArry::CPDArry()
{
}

CPDArry::~CPDArry()
{
	CloseDevice();
}

BOOL CPDArry::Open(CString strID, CString & strMsg)
{
	KLST_HANDLE deviceList = NULL;
	KLST_DEVINFO_HANDLE deviceInfo = NULL;
	DWORD dwErrCode = ERROR_SUCCESS;
	UINT nCount = 0;
	BOOL bFind = FALSE;
	CString strSerialNumber;
	int i;
	try
	{
		CloseDevice();
		if (!LstK_Init(&deviceList, KLST_FLAG_NONE))
		{
			dwErrCode = GetLastError();
			strMsg.Format("An error occured getting the device list! ErrorCode:%08Xh\n", dwErrCode);
			return FALSE;
		}	
		LstK_Count(deviceList, &nCount);
		if (nCount < 1)
		{
			strMsg.Format("No devices connected.\n");
			LstK_Free(deviceList);
			return FALSE;
		}
		LstK_MoveReset(deviceList);
		for (i = 0; i < nCount; i++)
		{
			LstK_MoveNext(deviceList, &deviceInfo);
			//strSerialNumber = deviceInfo->SerialNumber;			
			strSerialNumber = deviceInfo->Common.InstanceID;
			//驱动号：49A5
			if (deviceInfo->Common.Vid == 0x05A6 && deviceInfo->Common.Pid == 0x49A5)
			{				
				//USB端口层级及端口号(可配置)：6&1F533DAF&0&2,6&1F533DAF&0&1
				if (strSerialNumber.Find(strID) != -1)
				{
					m_deviceList = deviceList;
					m_deviceInfo = deviceInfo;
					bFind = TRUE;
					break;
				}				
			}
		}

		if (!bFind)
		{
			LstK_Free(deviceList);
			strMsg.Format("Don't Find the Device:%s", strID);
			return FALSE;
		}

		//This example will use the dynamic driver api so that it can be used	with all supported drivers.
		if (!LibK_LoadDriverAPI(&UsbAPI, m_deviceInfo->DriverID))
		{
			dwErrCode = GetLastError();
			strMsg.Format("LibK_LoadDriverAPI Failed! ErrorCode:%08Xh\n", dwErrCode);
			CloseDevice();
			return FALSE;
		}

		//Open the device. This creates the physical USB device handle.	
		if (!UsbAPI.Init(&m_usbHandle, m_deviceInfo))
		{
			dwErrCode = GetLastError();
			strMsg.Format("UsbAPI.Init failed! ErrorCode:0x%08X\n", dwErrCode);
			CloseDevice();
			return FALSE;
		}

		return TRUE;
	}
	catch(...)
	{
		strMsg.Format("Exception:Open PD Array:%s", strID);
		return FALSE; 
	}
}

//设置高功率模式
BOOL CPDArry::Inititalize(CString & strMsg)
{
	SetMFGMode(strMsg);
	Sleep(500);
	return SetPowerMode(TRUE, strMsg);
}

void CPDArry::CloseDevice()
{
	//	Close the usb handle. If usbHandle is invalid (NULL), has no effect.	
	if (m_usbHandle)
	{
		UsbAPI.Free(m_usbHandle);
		m_usbHandle = NULL;
	}

	//	Free the device list. If deviceList is invalid (NULL), has no effect.
	LstK_Free(m_deviceList);
	m_deviceList = NULL;
}

BOOL CPDArry::GetTestDeviceEx(KLST_HANDLE* DeviceList,
	KLST_DEVINFO_HANDLE* DeviceInfo,
	DWORD dwVID,
	DWORD dwPID,
	KLST_FLAG Flags)
{
	UINT deviceCount = 0;
	KLST_HANDLE deviceList = NULL;
	KLST_DEVINFO_HANDLE deviceInfo = NULL;

	// init
	*DeviceList = NULL;
	*DeviceInfo = NULL;

	// Get the device list
	if (!LstK_Init(&deviceList, Flags))
	{
		printf("Error initializing device list.\n");
		return FALSE;
	}

	LstK_Count(deviceList, &deviceCount);
	if (!deviceCount)
	{
		printf("Device list empty.\n");
		SetLastError(ERROR_DEVICE_NOT_CONNECTED);

		// If LstK_Init returns TRUE, the list must be freed.
		LstK_Free(deviceList);

		return FALSE;
	}

	// printf("Looking for device vid/pid %04X/%04X..\n", vidArg, pidArg);

	LstK_FindByVidPid(deviceList, dwVID, dwPID, &deviceInfo);

	if (deviceInfo)
	{
		// This function returns the device list and the device info
		// element which matched.  The caller is responsible for freeing
		// this list when it is no longer needed.
		*DeviceList = deviceList;
		*DeviceInfo = deviceInfo;

		// Report the connection state of the example device
		printf("Using %04X:%04X (%s): %s - %s\n",
			deviceInfo->Common.Vid,
			deviceInfo->Common.Pid,
			deviceInfo->Common.InstanceID,
			deviceInfo->DeviceDesc,
			deviceInfo->Mfg);

		return TRUE;
	}
	else
	{
		// Display some simple usage information for the example applications.
		CHAR programPath[MAX_PATH] = { 0 };
		PCHAR programExe = programPath;
		GetModuleFileNameA(GetModuleHandleA(NULL), programPath, sizeof(programPath));
		while (strpbrk(programExe, "\\/")) programExe = strpbrk(programExe, "\\/") + 1;
		//	printf("Device vid/pid %04X/%04X not found.\n\n", vidArg, pidArg);
		//	printf("USAGE: %s vid=%04X pid=%04X\n\n", programExe, vidArg, pidArg);

		// If LstK_Init returns TRUE, the list must be freed.
		LstK_Free(deviceList);

		return FALSE;
	}

	return TRUE;
}

BOOL CPDArry::SetCmd(WINUSB_SETUP_PACKET* pstSetupPkt, BYTE *pbData, CString & strMsg)
{
	try
	{
		if (m_usbHandle == NULL || pbData == NULL)
		{
			strMsg.Format("SetCmd failed. m_usbHandle or pbData is NULL!");
			return FALSE;
		}

		if (UsbAPI.ControlTransfer(m_usbHandle, *pstSetupPkt, pbData, pstSetupPkt->Length, NULL, NULL))
		{	
			return TRUE;
		}
		strMsg.Format("sending command failed. Command:0x%02X-0x%02X-0x%04X", 
			pstSetupPkt->RequestType, pstSetupPkt->Request, pstSetupPkt->Value);
		return FALSE;
	}
	catch (...)
	{
		strMsg.Format("sending command Exception. Command:0x%02X-0x%02X-0x%04X", pstSetupPkt->RequestType, pstSetupPkt->Request, pstSetupPkt->Value);
		return FALSE;
	}

}

BOOL CPDArry::ShowAllDevice(WORD* pwListCount, CString* pcInfo)
{
	KLST_HANDLE deviceList = NULL;
	KLST_DEVINFO_HANDLE deviceInfo = NULL;
	DWORD errorCode = ERROR_SUCCESS;
	UINT count = 0;
	//char cData[256];
	DWORD dwIndex;

	/*
	Initialize a new LstK (device list) handle.
	The list is polulated with all usb devices libusbK can access.
	*/
	if (!LstK_Init(&deviceList, KLST_FLAG_NONE))
	{
		errorCode = GetLastError();
		printf("An error occured getting the device list. errorCode=%08Xh\n", errorCode);
		return errorCode;
	}

	// Get the number of devices contained in the device list.
	LstK_Count(deviceList, &count);
	*pwListCount = count;
	if (!count)
	{
		printf("No devices connected.\n");

		// Always free the device list if LstK_Init returns TRUE
		LstK_Free(deviceList);

		return ERROR_DEVICE_NOT_CONNECTED;
	}
	LstK_MoveReset(deviceList);
	for (dwIndex = 0; dwIndex < count; dwIndex++)
	{
		LstK_MoveNext(deviceList, &deviceInfo);
		pcInfo[dwIndex] = deviceInfo->Common.InstanceID;
		//ZeroMemory(pcInfo, sizeof(pcInfo));
		//sprintf_s(pcInfo, sizeof(pcInfo), "%s\n",
			//deviceInfo->Common.InstanceID
			//);
		//pcInfo += MAX_PATH;
	}

	//	Set to the first
	LstK_MoveReset(deviceList);
	LstK_MoveNext(deviceList, &deviceInfo);
	// Free the device list
	LstK_Free(deviceList);

	// return the win32 error code.
	return errorCode;
}

BOOL CPDArry::GetDescriptors(BYTE bType, BYTE* pbData, DWORD* pdwGetLength)
{
	KLST_HANDLE deviceList = NULL;			// device list handle (the list of device infos)
	KLST_DEVINFO_HANDLE deviceInfo = NULL;	// device info handle (the device list element)
	KUSB_HANDLE usbHandle = NULL;				// device interface usbHandle (the opened USB device)
	DWORD errorCode = ERROR_SUCCESS;
	WINUSB_SETUP_PACKET Pkt;
	KUSB_SETUP_PACKET* kPkt = (KUSB_SETUP_PACKET*)&Pkt;
	BYTE bGetData[4096];
	DWORD dwLength;
	DESCRIPTOR_ITERATOR descIterator;
	DWORD dwLastLength;


	if (bType == USB_DESCRIPTOR_TYPE_DEVICE || bType == USB_DESCRIPTOR_TYPE_CONFIGURATION)
	{
		//	Get device descriptor
		memset(&Pkt, 0, sizeof(Pkt));
		kPkt->BmRequest.Dir = BMREQUEST_DIR_DEVICE_TO_HOST;
		kPkt->Request = USB_REQUEST_GET_DESCRIPTOR;
		kPkt->ValueHi = bType;
		kPkt->ValueLo = 0; // Index
		kPkt->Length = sizeof(bGetData);
		if (!UsbAPI.ControlTransfer(m_usbHandle, Pkt, bGetData, sizeof(bGetData), (unsigned int*)&dwLength, NULL))
		{
			errorCode = GetLastError();
			CloseDevice();
			return FALSE;
		}
		*pdwGetLength = dwLength;
		memcpy(pbData, bGetData, dwLength);
	}
	else
	{
		// Get config descriptor
		memset(&Pkt, 0, sizeof(Pkt));
		kPkt->BmRequest.Dir = BMREQUEST_DIR_DEVICE_TO_HOST;
		kPkt->Request = USB_REQUEST_GET_DESCRIPTOR;
		kPkt->ValueHi = USB_DESCRIPTOR_TYPE_CONFIGURATION;
		kPkt->ValueLo = 0; // Index
		kPkt->Length = sizeof(bGetData);
		if (!UsbAPI.ControlTransfer(m_usbHandle, Pkt, bGetData, sizeof(bGetData), (unsigned int*)&dwLength, NULL))
		{
			errorCode = GetLastError();
			CloseDevice();
			return FALSE;
		}

		if (!InitDescriptorIterator(&descIterator, bGetData, dwLength))
		{
			errorCode = GetLastError();
			CloseDevice();
			return FALSE;
		}

		dwLastLength = dwLength;
		while (NextDescriptor(&descIterator))
		{
			if (descIterator.Ptr.Common->bDescriptorType == bType)
			{
				*pdwGetLength = dwLastLength - descIterator.Remaining;
				memcpy(pbData, descIterator.Ptr.Bytes, *pdwGetLength);
				break;
			}


		}
	}

	return TRUE;
}

BOOL CPDArry::InitDescriptorIterator(PDESCRIPTOR_ITERATOR descIterator, BYTE* configDescriptor, DWORD lengthTransferred)
{
	memset(descIterator, 0, sizeof(descIterator));
	descIterator->Ptr.Bytes = configDescriptor;
	descIterator->Remaining = descIterator->Ptr.Config->wTotalLength;

	if (lengthTransferred > sizeof(USB_CONFIGURATION_DESCRIPTOR) && lengthTransferred >= descIterator->Ptr.Config->wTotalLength)
	{
		if (descIterator->Ptr.Config->wTotalLength >= sizeof(USB_CONFIGURATION_DESCRIPTOR) + sizeof(USB_INTERFACE_DESCRIPTOR))
			return TRUE;
	}

	SetLastError(ERROR_BAD_LENGTH);
	descIterator->Remaining = 0;
	return FALSE;
}

BOOL CPDArry::NextDescriptor(PDESCRIPTOR_ITERATOR descIterator)
{
	if (descIterator->Remaining >= sizeof(USB_COMMON_DESCRIPTOR) && descIterator->Remaining >= descIterator->Ptr.Common->bLength)
	{
		descIterator->Remaining -= descIterator->Ptr.Common->bLength;
		if (descIterator->Remaining >= sizeof(USB_COMMON_DESCRIPTOR))
		{
			descIterator->Ptr.Bytes += descIterator->Ptr.Common->bLength;
			return TRUE;
		}
	}
	descIterator->Remaining = 0;
	SetLastError(ERROR_NO_MORE_ITEMS);
	return FALSE;
}

//设置高功率模式，打开后设置一次即可
BOOL CPDArry::SetPowerMode(BOOL bHigh, CString & strMsg)
{
	WINUSB_SETUP_PACKET stSetupPkt;
	BYTE	bRxData[512];
	BYTE	pbTempData[16];
	CString StrNotice = "";

	ZeroMemory(bRxData, sizeof(bRxData));
	bRxData[0] = 1;

	stSetupPkt.RequestType = 0x44;
	stSetupPkt.Request = 0x00;
	stSetupPkt.Value = 0x0001;
	stSetupPkt.Index = 0x0000;
	stSetupPkt.Length = 0x0001;

	if (!SetCmd(&stSetupPkt, bRxData, strMsg))
	{		
		StrNotice.Format("Set Power Mode failed! %s", strMsg);
		strMsg = StrNotice;
		return TRUE;
	}
	return TRUE;
	
	
}

BOOL CPDArry::GetPowerMode(CString & strMsg)
{
	WINUSB_SETUP_PACKET stSetupPkt;
	BYTE	bRxData[256];
	BYTE	pbTempData[16];
	CString StrNotice = "";

	ZeroMemory(bRxData, sizeof(bRxData));
	stSetupPkt.RequestType = 0xC4;
	stSetupPkt.Request = 0x01;
	stSetupPkt.Value = 0x0002;
	stSetupPkt.Index = 0x0000;
	stSetupPkt.Length = 0x0001;
	if (!SetCmd(&stSetupPkt, bRxData, strMsg))
	{
		StrNotice.Format("send power command %s", strMsg);
		strMsg = StrNotice;
		return TRUE;
	}
	Sleep(300);
	if (1 == bRxData[0])
	{
		return TRUE;
	}
	else
	{
		strMsg.Format("Set Power Mode failed! The obtained value is different from the set value");
		return TRUE;
	}

	
}

BOOL CPDArry::SetMFGMode(CString & strMsg)
{
	WINUSB_SETUP_PACKET stSetupPkt;
	BYTE	pbTempData[16];
	CString StrNotice = "";
	char cData[8] = "MFG_CMD";
	stSetupPkt.RequestType = 0x44;
	stSetupPkt.Request = 0xFF;
	stSetupPkt.Value = 0x0000;
	stSetupPkt.Index = 0x0000;
	stSetupPkt.Length = 0x0008;

	if (!SetCmd(&stSetupPkt, (BYTE*)cData, strMsg))
	{
		StrNotice.Format("Set Power Mode failed! %s", strMsg);
		strMsg = StrNotice;
		return FALSE;
	}
	return TRUE;
	

}

BOOL CPDArry::GetActualPower(double* pdPower, int nChannelCount, CString & strMsg)
{
	WINUSB_SETUP_PACKET stSetupPkt;
	BYTE	bRxData[512];
	BYTE	pbTempData[16];

	try
	{
		if (pdPower == NULL)
		{
			strMsg.Format("GetActualPower failed.pdPower is NULL.");
			return FALSE;
		}

		if (nChannelCount > MAX_PD_CHANNEL)
		{
			strMsg.Format("GetActualPower failed.Power buffer is more than the maximum(30).");
			return FALSE;
		}
	
		ZeroMemory(bRxData, sizeof(bRxData));
		ZeroMemory(pbTempData, sizeof(pbTempData));

		stSetupPkt.RequestType = 0xC4;
		stSetupPkt.Request = 0xFF;
		stSetupPkt.Value = 0x000A;
		stSetupPkt.Index = 0x0000;
		stSetupPkt.Length = nChannelCount * 2;

		if (SetCmd(&stSetupPkt, bRxData, strMsg))
		{
			
			for (int nChannel = 0; nChannel < nChannelCount; nChannel++)
			{
				memcpy(pbTempData, bRxData + nChannel * 2, 2);

				short wPower = (pbTempData[1] << 8) + (pbTempData[0]);

				pdPower[nChannel] = (double)wPower / 100.0;
			}
			return TRUE;
		}

		return FALSE;
	}
	catch (...)
	{
		return FALSE;
	}
}