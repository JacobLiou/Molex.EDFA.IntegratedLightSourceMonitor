#pragma once

#if !defined(AFX_USB_H__539B03F8_CD68_4EE3_994A_FF01856B6107__INCLUDED_)
#define AFX_USB_H__539B03F8_CD68_4EE3_994A_FF01856B6107__INCLUDED_

#include "libusbk.h"

#if _MSC_VER > 1000
#pragma once
#endif // _MSC_VER > 1000

#define USB_DESCRIPTOR_TYPE_HID			0x21
#define MAX_PD_CHANNEL 33

typedef struct tagDeviceDescriptor
{
	BYTE bLength;
	BYTE bType;
	WORD wBcdUsb;
	BYTE bDevClass;
	BYTE bDevSubClass;
	BYTE bDevProtocol;
	BYTE bMaxPktSize;
	WORD wVID;
	WORD wPID;
	WORD wBcdDev;
	BYTE bIManuf;
	BYTE bIProduct;
	BYTE bISN;
	BYTE bNumCfg;
}stDeviceDescriptor;


typedef struct _DESCRIPTOR_ITERATOR
{
	LONG	Remaining;

	union
	{
		PUCHAR							Bytes;
		PUSB_COMMON_DESCRIPTOR			Common;
		PUSB_CONFIGURATION_DESCRIPTOR	Config;
		PUSB_INTERFACE_DESCRIPTOR		Interface;
		PUSB_ENDPOINT_DESCRIPTOR		Endpoint;
	} Ptr;
} DESCRIPTOR_ITERATOR, *PDESCRIPTOR_ITERATOR;

class CPDArry
{
public:
	CPDArry();
	virtual ~CPDArry();

	virtual BOOL Open(CString strID, CString & strMsg);
	virtual BOOL Inititalize(CString & strMsg);	
	virtual BOOL GetPowerMode(CString & strMsg);
	virtual void CloseDevice();
	virtual BOOL GetActualPower(double* pdPower, int nChannelCount, CString & strMsg);
	virtual BOOL ShowAllDevice(WORD* pwListCount, CString* pcInfo);

private:
	KUSB_DRIVER_API		UsbAPI;
	KUSB_HANDLE			m_usbHandle = NULL;
	KLST_HANDLE			m_deviceList = NULL;			// device list handle (the list of device infos)
	KLST_DEVINFO_HANDLE m_deviceInfo = NULL;

	BOOL SetCmd(WINUSB_SETUP_PACKET * pstSetupPkt, BYTE *pbData, CString & strMsg);
	BOOL SetPowerMode(BOOL bHigh, CString & strMsg);
	BOOL SetMFGMode(CString & strMsg);
	BOOL GetTestDeviceEx(KLST_HANDLE* DeviceList,
		KLST_DEVINFO_HANDLE* DeviceInfo,
		DWORD dwVID,
		DWORD dwPID,
		KLST_FLAG Flags);	
	
	BOOL InitDescriptorIterator(PDESCRIPTOR_ITERATOR descIterator, BYTE* configDescriptor, DWORD lengthTransferred); 
	BOOL NextDescriptor(PDESCRIPTOR_ITERATOR descIterator);
	BOOL GetDescriptors(BYTE bType, BYTE* pbData, DWORD* pdwGetLength);
};

#endif // !defined(AFX_USB_H__539B03F8_CD68_4EE3_994A_FF01856B6107__INCLUDED_)



