
// MFCApplication1Dlg.cpp : implementation file
//

#include "stdafx.h"
#include "MFCApplication1.h"
#include "MFCApplication1Dlg.h"
#include "afxdialogex.h"
#include "PDArry.h"
#ifdef _DEBUG
#define new DEBUG_NEW
#endif
using namespace std;
#import "C:\Public-T\Software\UDL\UDL2_Server.dll" no_namespace
IUDL2_EnginePtr m_pEngine;
IUDL2_WMPtr    m_pWM;

#define COLOR_DEFAULT 0 //默认颜色
#define COLOR_RED 1 //红色
#define COLOR_BLUE 2 //蓝色

double PD_Power_Delta;
double WM_Delta;
BOOL Test_Terminate_flag = FALSE;
BOOL Alarm_Falg = FALSE;
// CAboutDlg dialog used for App About

class CAboutDlg : public CDialogEx
{
public:
	CAboutDlg();

// Dialog Data
#ifdef AFX_DESIGN_TIME
	enum { IDD = IDD_ABOUTBOX };
#endif

	protected:
	virtual void DoDataExchange(CDataExchange* pDX);    // DDX/DDV support

// Implementation
protected:
	DECLARE_MESSAGE_MAP()
};

CAboutDlg::CAboutDlg() : CDialogEx(IDD_ABOUTBOX)
{
}

void CAboutDlg::DoDataExchange(CDataExchange* pDX)
{
	CDialogEx::DoDataExchange(pDX);
	
}

BEGIN_MESSAGE_MAP(CAboutDlg, CDialogEx)
END_MESSAGE_MAP()


// CMFCApplication1Dlg dialog



CMFCApplication1Dlg::CMFCApplication1Dlg(CWnd* pParent /*=NULL*/)
	: CDialogEx(IDD_MFCAPPLICATION1_DIALOG, pParent)
	, test_min_set()	
{
	m_hIcon = AfxGetApp()->LoadIcon(IDR_MAINFRAME);
}

void CMFCApplication1Dlg::DoDataExchange(CDataExchange* pDX)
{
	CDialogEx::DoDataExchange(pDX);
	DDX_Text(pDX, IDC_EDIT1, test_min_set);
	DDX_Control(pDX, IDC_BUTTON3, button_color);
	DDX_Control(pDX, IDC_LIST1, m_listctrl);
	DDX_Control(pDX, IDC_LIST3, m_listctrl2);
	DDX_Control(pDX, IDC_COMBO1, m_cbExamble);
	DDX_Control(pDX, IDC_TREE1, m_tree);
	
}

BEGIN_MESSAGE_MAP(CMFCApplication1Dlg, CDialogEx)
	ON_WM_SYSCOMMAND()
	ON_WM_PAINT()
	ON_WM_QUERYDRAGICON()
	ON_BN_CLICKED(IDC_BUTTON1, &CMFCApplication1Dlg::OnBnClickedButton1)
	ON_BN_CLICKED(IDC_TREE1, &CMFCApplication1Dlg::OnTvnSelchangedTree1)
	ON_NOTIFY(NM_CUSTOMDRAW, IDC_LIST1, &CMFCApplication1Dlg::OnNMCustomdrawList1)
	ON_NOTIFY(NM_CUSTOMDRAW, IDC_LIST3, &CMFCApplication1Dlg::OnNMCustomdrawList1)
	ON_BN_CLICKED(IDC_BUTTON2, &CMFCApplication1Dlg::OnBnClickedButton2)
	//ON_BN_CLICKED(IDC_BUTTON4, &CMFCApplication1Dlg::OnBnClickedButton4)
END_MESSAGE_MAP()


// CMFCApplication1Dlg message handlers

BOOL CMFCApplication1Dlg::OnInitDialog()
{
	CDialogEx::OnInitDialog();

	// Add "About..." menu item to system menu.

	// IDM_ABOUTBOX must be in the system command range.
	ASSERT((IDM_ABOUTBOX & 0xFFF0) == IDM_ABOUTBOX);
	ASSERT(IDM_ABOUTBOX < 0xF000);

	CMenu* pSysMenu = GetSystemMenu(FALSE);
	if (pSysMenu != NULL)
	{
		BOOL bNameValid;
		CString strAboutMenu;
		bNameValid = strAboutMenu.LoadString(IDS_ABOUTBOX);
		ASSERT(bNameValid);
		if (!strAboutMenu.IsEmpty())
		{
			pSysMenu->AppendMenu(MF_SEPARATOR);
			pSysMenu->AppendMenu(MF_STRING, IDM_ABOUTBOX, strAboutMenu);
		}
	}

	// Set the icon for this dialog.  The framework does this automatically
	//  when the application's main window is not a dialog
	SetIcon(m_hIcon, TRUE);			// Set big icon
	SetIcon(m_hIcon, FALSE);		// Set small icon

	OnTvnSelchangedTree1();
	button_color.m_bTransparent = false;
	button_color.m_bDontUseWinXPTheme = true;
	button_color.SetFaceColor(RGB(0, 255, 0), true);//绿色
	GetDlgItem(IDC_BUTTON3)->SetWindowText("无告警");

	UpdateData(true);
	m_listctrl.DeleteAllItems();
	m_listctrl.SetExtendedStyle(m_listctrl.GetExtendedStyle() | LVS_EX_FULLROWSELECT | LVS_EX_GRIDLINES);
	m_listctrl.InsertColumn(0, _T("PD Array SN"), LVCFMT_LEFT, 155);
	m_listctrl.InsertColumn(1, _T("PD Item"), LVCFMT_LEFT, 65);
	m_listctrl.InsertColumn(2, _T("Alarm"), LVCFMT_LEFT, 65);
	m_listctrl.InsertColumn(3, _T("Value"), LVCFMT_LEFT, 85);
	m_listctrl.InsertColumn(4, _T("Max"), LVCFMT_LEFT, 85);
	m_listctrl.InsertColumn(5, _T("Min"), LVCFMT_LEFT, 85);
	m_listctrl.InsertColumn(6, _T("Delta"), LVCFMT_LEFT, 85);
	m_listctrl.InsertColumn(7, _T("Spec Delta"), LVCFMT_LEFT, 85);

	m_listctrl2.DeleteAllItems();
	m_listctrl2.SetExtendedStyle(m_listctrl2.GetExtendedStyle() | LVS_EX_FULLROWSELECT | LVS_EX_GRIDLINES);
	m_listctrl2.InsertColumn(0, _T("WM Item"), LVCFMT_LEFT, 100);
	m_listctrl2.InsertColumn(1, _T("Alarm"), LVCFMT_LEFT, 100);
	m_listctrl2.InsertColumn(2, _T("Value"), LVCFMT_LEFT, 100);
	m_listctrl2.InsertColumn(3, _T("Spec Value"), LVCFMT_LEFT, 100);
	m_listctrl2.InsertColumn(4, _T("Delta"), LVCFMT_LEFT, 100);
	m_listctrl2.InsertColumn(5, _T("Spec Delta"), LVCFMT_LEFT, 100);
	UpdateData(false);

	TCHAR m_tszAppFolder[1024];
	CString strValue, strFileName, str2,str_SN;
	CString str1;
	int j = 0;
	
	GetCurrentDirectory(sizeof(m_tszAppFolder), m_tszAppFolder);
	strFileName.Format("%s\\config\\PDConfig.ini", m_tszAppFolder);
	
	UpdateData(true);
	GetPrivateProfileString(_T("SN_Config"), _T("SN_Num"),
		"ERROR", (char*)(LPCTSTR)strValue, MAX_PATH, strFileName);
	PDArray_Num = atoi(strValue);

	GetPrivateProfileString(_T("SN_Config"), "PD_Power_Delta",
		"ERROR", (char*)(LPCTSTR)strValue, MAX_PATH, strFileName);
	PD_Power_Delta = atof(strValue);

	for (int pd_arr_num = 0; pd_arr_num < PDArray_Num; pd_arr_num++)
	{	
		str1.Format(_T("PD_Array%d"), pd_arr_num + 1);
		GetPrivateProfileString(_T(str1), "SN","ERROR", (char*)(LPCTSTR)strValue, MAX_PATH, strFileName);
		str_SN.Format(_T("%s"), strValue);

		for (int i = 0; i < 32; i++)
		{
			str2.Format("PD%d", i + 1);
			str1.Format(_T("PD_Array%d"), pd_arr_num + 1);
			GetPrivateProfileString(_T(str1), str2,"ERROR", (char*)(LPCTSTR)strValue, MAX_PATH, strFileName);
			if (atoi(strValue) != 0)
			{	
				m_listctrl.InsertItem(j, str_SN);

				str1.Format(_T("%d"), i + 1);
				m_listctrl.SetItemText(j, 1, str1);

				str1 = _T("OFF");
				m_listctrl.SetItemText(j, 2, str1);

				str1 = _T("0.0");
				m_listctrl.SetItemText(j, 3, str1);

				str1 = _T("0.0");
				m_listctrl.SetItemText(j, 4, str1);

				str1 = _T("0.0");
				m_listctrl.SetItemText(j, 5, str1);

				str1 = _T("0.0");
				m_listctrl.SetItemText(j, 6, str1);
				
				str1.Format(_T("%.2f"), PD_Power_Delta);
				m_listctrl.SetItemText(j, 7, str1);

				j++;
			}
		}
	}
	
	GetCurrentDirectory(sizeof(m_tszAppFolder), m_tszAppFolder);
	strFileName.Format("%s\\config\\WMConfig.ini", m_tszAppFolder);

	UpdateData(true);
	GetPrivateProfileString(_T("WM_Config"), _T("WM_Num"),
		"ERROR", (char*)(LPCTSTR)strValue, MAX_PATH, strFileName);
	WMArray_Num = atoi(strValue);

	GetPrivateProfileString(_T("WM_Config"), "WM_Delta",
		"ERROR", (char*)(LPCTSTR)strValue, MAX_PATH, strFileName);
	WM_Delta = atof(strValue);

	for (int wm_arr_num = 0; wm_arr_num < WMArray_Num; wm_arr_num++)
	{
		str1.Format(_T("%d"), wm_arr_num + 1);
		m_listctrl2.InsertItem(wm_arr_num, str1);

		str1 = _T("OFF");
		m_listctrl2.SetItemText(wm_arr_num, 1, str1);

		str1 = _T("0.0");
		m_listctrl2.SetItemText(wm_arr_num, 2, str1);

		str1.Format(_T("WM%d"), wm_arr_num + 1);
		GetPrivateProfileString(_T("WM_Config"), str1, "ERROR", (char*)(LPCTSTR)strValue, MAX_PATH, strFileName);
		m_listctrl2.SetItemText(wm_arr_num, 3, strValue);

		str1 = _T("0.0");
		m_listctrl2.SetItemText(wm_arr_num, 4, str1);

		str1.Format(_T("%.2f"), WM_Delta);
		m_listctrl2.SetItemText(wm_arr_num, 5, str1);
		
	}
	UpdateWindow();
	UpdateData(false);
	return TRUE;  // return TRUE  unless you set the focus to a control
}

void CMFCApplication1Dlg::OnSysCommand(UINT nID, LPARAM lParam)
{
	if ((nID & 0xFFF0) == IDM_ABOUTBOX)
	{
		CAboutDlg dlgAbout;
		dlgAbout.DoModal();
	}
	else
	{
		CDialogEx::OnSysCommand(nID, lParam);
	}
}

// If you add a minimize button to your dialog, you will need the code below
//  to draw the icon.  For MFC applications using the document/view model,
//  this is automatically done for you by the framework.

void CMFCApplication1Dlg::OnPaint()
{
	if (IsIconic())
	{
		CPaintDC dc(this); // device context for painting

		SendMessage(WM_ICONERASEBKGND, reinterpret_cast<WPARAM>(dc.GetSafeHdc()), 0);

		// Center icon in client rectangle
		int cxIcon = GetSystemMetrics(SM_CXICON);
		int cyIcon = GetSystemMetrics(SM_CYICON);
		CRect rect;
		GetClientRect(&rect);
		int x = (rect.Width() - cxIcon + 1) / 2;
		int y = (rect.Height() - cyIcon + 1) / 2;

		// Draw the icon
		dc.DrawIcon(x, y, m_hIcon);
	}
	else
	{
		CDialogEx::OnPaint();
	}
}

// The system calls this function to obtain the cursor to display while the user drags
//  the minimized window.
HCURSOR CMFCApplication1Dlg::OnQueryDragIcon()
{
	return static_cast<HCURSOR>(m_hIcon);
}



void CMFCApplication1Dlg::OnBnClickedButton1()
{
	WORD wCount;
	WORD wIndex;
	CString cListInfo[256];
	CPDArry PDArray1;
	CString str_SN;
	double max_power[1024];
	double min_power[1024];
	CString strMsg="OK";
	CString str2;
	CString str1;
	CString str3;
	CPDArry PDArray[256];
	BOOL result;
	CStdioFile outFile, outFile2;
	double pdPower[33] = { 0 };
	CTimeSpan timeSpan;
	CString filename, filename2;
	CString strValue, strFileName, strFileName_WM;
	TCHAR m_tszAppFolder[1024];
	CTime start_time = CTime::GetCurrentTime();
	WORD flag = 0;
	DWORD Test_times = 0;//DWORD的范围是0~4294967295，即0~(2-1)，不会溢出
	CString set_run_time;
	CString data_file_path;
	BOOL PD_SN_flag = FALSE;

	HRESULT hr;
	double WM_max_wave, WM_min_wave;
	double pdblSignalPower3[1000] = { 0 };
	double pdbMeasResult3[1000] = { 0 };
	long   plWLCount_WM = 0;


	m_cbExamble.GetWindowText(set_run_time);
	Test_Terminate_flag = FALSE;//初始化

	strFileName_WM.Format("%s\\config\\WMConfig.ini", m_tszAppFolder);
	str1.Format(_T("WM%d"), WMArray_Num);
	GetPrivateProfileString(_T("WM_Config"), str1, "ERROR", (char*)(LPCTSTR)strValue, MAX_PATH, strFileName_WM);
	WM_max_wave= atof(strValue);
	str1.Format(_T("WM1"), WMArray_Num);
	GetPrivateProfileString(_T("WM_Config"), str1, "ERROR", (char*)(LPCTSTR)strValue, MAX_PATH, strFileName_WM);
	WM_min_wave = atof(strValue);


	GetDlgItem(IDC_BUTTON1)->SetWindowText("运行中");
	UpdateData(false);
	UpdateWindow();
	UpdateData(true);

	//初始化PD Array配置
	PDArray1.CloseDevice();
	PDArray1.ShowAllDevice(&wCount, cListInfo);
	PDArray1.CloseDevice();//找到PD Array SN
	GetCurrentDirectory(sizeof(m_tszAppFolder), m_tszAppFolder);
	strFileName.Format("%s\\config\\PDConfig.ini", m_tszAppFolder);
	for (int pd_arr_num = 0; pd_arr_num < PDArray_Num; pd_arr_num++)
	{
		str1.Format(_T("PD_Array%d"), pd_arr_num + 1);
		GetPrivateProfileString(_T(str1), "SN", "ERROR", (char*)(LPCTSTR)strValue, MAX_PATH, strFileName);
		str_SN.Format(_T("%s"), strValue);
		PD_SN_flag = FALSE;
		for (wIndex = 0; wIndex < wCount; wIndex++)
		{
			str1 = cListInfo[wIndex];
			if (str_SN ==str1)//确保与PDconfig.ini里面的SN1，2相对应
			{
				result = PDArray[pd_arr_num].Open(str_SN, strMsg);
				if (result == false)
				{
					MessageBox(strMsg);
					return;
				}
				Sleep(1000);
				result = PDArray[pd_arr_num].Inititalize(strMsg);
				if (result == false)
				{
					MessageBox(strMsg);
					return;
				}
				Sleep(1000);
				result = PDArray[pd_arr_num].GetPowerMode(strMsg);
				if (result == false)
				{
					MessageBox(strMsg);
					return;
				}
				Sleep(1000);
				PD_SN_flag = TRUE;
			}
			
		}
		if (PD_SN_flag == FALSE)
		{
			strMsg = "没找到PD Array!";
			GetDlgItem(IDC_BUTTON1)->SetWindowText("Start");
			MessageBox(strMsg);
			return;
		}
	}


	WM_init();//初始化WM UDL配置
	hr = m_pWM->SetWMParameters(1, 1, WM_min_wave-3, WM_max_wave+3, 10.0, 5.0);
	if (hr == S_FALSE)
	{
		MessageBox(_T("WM SetWMParameters输入有误!"));
		return;
	}
	Sleep(100);
	hr = m_pWM->ExecuteWMSingleSweep(1);
	if (hr == S_FALSE)
	{
		MessageBox(_T("ExecuteWMSingleSweep输入有误!"));
		return;
	}
	Sleep(100);
	hr = m_pWM->GetWMChResult(1, &plWLCount_WM, pdbMeasResult3, pdblSignalPower3);
	if (hr == S_FALSE)
	{
		MessageBox(_T("GetWDMChResult输入有误!"));
		return;
	}
	if (plWLCount_WM != WMArray_Num)
	{
		MessageBox(_T("波长计测试波长数量不对"));
		return;
	}

	data_file_path.Format(_T(".\\data\\%s"), start_time.Format("%Y-%m-%d-%H-%M"));
	::CreateDirectory(data_file_path, NULL);//以程序运行时间年-月-日-分-时 创建目录

	//初始化PD Array最大最小值
	for (wIndex = 0; wIndex < wCount; wIndex++)
	{
		Sleep(1000);
		result = PDArray[wIndex].GetActualPower(pdPower, 33, strMsg);
		if (result == false)
		{
			MessageBox(strMsg);
			return;
		}
		for (int i = 0; i < 32; i++)
		{
			str1.Format(_T("PD_Array%d"), wIndex + 1);
			str3.Format(_T("PD%d"), i + 1);
			GetPrivateProfileString(_T(str1), str3, "ERROR", (char*)(LPCTSTR)strValue, MAX_PATH, strFileName);
			if (strValue.Find("0") == -1)
			{
				max_power[flag] = pdPower[i];
				min_power[flag] = pdPower[i];

				str1.Format(_T("%.2f"), max_power[flag]);
				m_listctrl.SetItemText(flag, 3, str1);

				str1.Format(_T("%.2f"), min_power[flag]);
				m_listctrl.SetItemText(flag, 4, str1);

				flag++;
			}
			
		}
	}
	
	

	while (1)
	{
		CTime time1 = CTime::GetCurrentTime();//标记为时间1
		filename.Format(_T(".\\data\\%s\\%s_data.csv"), start_time.Format("%Y-%m-%d-%H-%M"), time1.Format("%Y-%m-%d-%H-%M"));
		outFile.Open(filename, CFile::modeCreate | CFile::modeWrite);
		outFile.SeekToEnd();
		str2.Format(_T("LocalTime,"));
		for (wIndex = 0; wIndex < wCount; wIndex++)
		{
			for (int i = 0; i < 32; i++)
			{
				str1.Format(_T("PD_Array%d"), wIndex + 1);
				str3.Format(_T("PD%d"), i + 1);
				GetPrivateProfileString(_T(str1), str3, "ERROR", (char*)(LPCTSTR)strValue, MAX_PATH, strFileName);
				if (strValue.Find("0") == -1)
				{
					str1.Format(_T("PA%d %d,"), wIndex + 1, i + 1);
					str2 += str1;
				}
			}
		}
		str2 += "\n";
		outFile.WriteString(str2);//初始化数据表头

		str2.Format(_T("LocalTime,"));
		filename2.Format(_T(".\\data\\%s\\%s_data_WM.csv"), start_time.Format("%Y-%m-%d-%H-%M"), time1.Format("%Y-%m-%d-%H-%M"));
		outFile2.Open(filename2, CFile::modeCreate | CFile::modeWrite);
		outFile2.SeekToEnd();
		for (int i = 0; i <WMArray_Num; i++)
		{			
			str1.Format(_T("WM%d,"),i + 1);
			str2 += str1;			
		}
		str2 += "\n";
		outFile2.WriteString(str2);//初始化数据表头


		while (1)
		{
			CTime time2 = CTime::GetCurrentTime();
			flag = 0;//与Data Table的行数一一对应
			str2 = time2.Format("%Y-%m-%d:%H:%M:%S,");
			for (wIndex = 0; wIndex < wCount; wIndex++)
			{
				Sleep(2000);
				result = PDArray[wIndex].GetActualPower(pdPower, 33, strMsg);
				if (result == false)
				{
					MessageBox(strMsg);
					return;
				}
				for (int i = 0; i < 32; i++)
				{
					str1.Format(_T("PD_Array%d"), wIndex + 1);
					str3.Format(_T("PD%d"), i + 1);
					GetPrivateProfileString(_T(str1), str3, "ERROR", (char*)(LPCTSTR)strValue, MAX_PATH, strFileName);
					if (strValue.Find("0") == -1)
					{
						str1.Format(_T("%f,"), pdPower[i]);
						str2 += str1;
						if (pdPower[i] > max_power[flag])
						{
							max_power[flag] = pdPower[i];
						}
						if (pdPower[i] < min_power[flag])
						{
							min_power[flag] = pdPower[i];
						}

						if (max_power[flag] - min_power[flag]>PD_Power_Delta)
						{
							str1 = _T("ON");
							Alarm_Falg = TRUE;//表示有告警
							button_color.SetFaceColor(RGB(255, 0, 0), true);//红色
							GetDlgItem(IDC_BUTTON3)->SetWindowText("有告警");
							m_listctrl.SetItemData(flag, COLOR_RED);//把有告警的某行设置为红色
						}
						else
						{
							str1 = _T("OFF");
						}

						m_listctrl.SetItemText(flag, 2, str1);

						str1.Format(_T("%.2f"), pdPower[i]);
						m_listctrl.SetItemText(flag, 3, str1);

						str1.Format(_T("%.2f"), max_power[flag]);
						m_listctrl.SetItemText(flag, 4, str1);

						str1.Format(_T("%.2f"), min_power[flag]);
						m_listctrl.SetItemText(flag, 5, str1);

						str1.Format(_T("%.2f"), max_power[flag] - min_power[flag]);
						m_listctrl.SetItemText(flag, 6, str1);
						flag++;
					}

				}
			}
			str2 += "\n";

			if (Alarm_Falg == TRUE)//有告警就一直记录数据
			{
				outFile.WriteString(str2);
			}
			else//没告警100次记录一次数据
			{
				if (Test_times%100==0)//
				{
					outFile.WriteString(str2);
				}				
			}
			if (Test_times % 100 == 0)//100次PD扫描再扫描一次波长数据
			{
				hr = m_pWM->ExecuteWMSingleSweep(1);
				if (hr == S_FALSE)
				{
					MessageBox(_T("ExecuteWMSingleSweep输入有误!"));
					return;
				}
				Sleep(100);
				hr = m_pWM->GetWMChResult(1, &plWLCount_WM, pdbMeasResult3, pdblSignalPower3);
				if (hr == S_FALSE)
				{
					MessageBox(_T("GetWDMChResult输入有误!"));
					return;
				}
				str2 = time2.Format("%Y-%m-%d:%H:%M:%S,");
				for (int i = 0; i < plWLCount_WM; i++)
				{					
					str3.Format(_T("WM%d"), i + 1);
					GetPrivateProfileString(_T("WM_Config"), str3, "ERROR", (char*)(LPCTSTR)strValue, MAX_PATH, 
						strFileName_WM);
					if (fabs(atof(strValue) - pdbMeasResult3[i])>WM_Delta)
					{
						str1 = _T("ON");
						Alarm_Falg = TRUE;//表示有告警
						button_color.SetFaceColor(RGB(255, 0, 0), true);//红色
						GetDlgItem(IDC_BUTTON3)->SetWindowText("有告警");
						m_listctrl2.SetItemData(i, COLOR_RED);//把有告警的某行设置为红色
					}
					else
					{
						str1 = _T("OFF");
					}
					m_listctrl2.SetItemText(i, 1, str1);

					str1.Format(_T("%.2f"), pdbMeasResult3[i]);
					m_listctrl2.SetItemText(i, 2, str1);

					str1.Format(_T("%.2f"), fabs(atof(strValue) - pdbMeasResult3[i]));
					m_listctrl2.SetItemText(i, 4, str1);
				}
				str2 += "\n";
				outFile2.WriteString(str2);
			}
			Test_times++;//测试扫描次数一直增加
			timeSpan = time2 - start_time;
			test_min_set = fabs(timeSpan.GetTotalMinutes());
			UpdateData(false);
			UpdateWindow();
			UpdateData(true);
			YieldToPeers();
			if (fabs((time2 - time1).GetTotalMinutes()) >= 24 * 60)//24小时后终止循环，设置新的数据文件名称
			{
				outFile.Close();
				outFile2.Close();
				break;
			}
			if (set_run_time.Find("long") == -1)//选择long-term表示长期运行不终止
			{
				if (test_min_set > _ttoi(set_run_time) * 60)
				{
					break;
				}
			}
			if (Test_Terminate_flag == TRUE)
			{
				break;
			}
		}
		if (set_run_time.Find("long") == -1)//选择long-term表示长期运行不终止
		{
			if (test_min_set > _ttoi(set_run_time) * 60)
			{
				break;
			}
		}
		if (Test_Terminate_flag == TRUE)
		{
			break;
		}
		
	}
	outFile.Close();
	outFile2.Close();
	GetDlgItem(IDC_BUTTON1)->SetWindowText("Start");
	UpdateData(false);
	UpdateWindow();
	UpdateData(true);
	strMsg = "测试完成！";
	MessageBox(strMsg);

}


void CMFCApplication1Dlg::OnTvnSelchangedTree1()
{
	/*HTREEITEM hParent, hChild;
	WORD wCount;
	WORD wIndex;
	CString cListInfo[256];
	CPDArry PDArray1;
	CString str_SN;
	//hParent = m_tree.InsertItem(_T("PD Array SN"), TVI_ROOT);	
	PDArray1.CloseDevice();
	PDArray1.ShowAllDevice(&wCount, cListInfo);
	for (wIndex = 0; wIndex < wCount; wIndex++)
	{
		str_SN = cListInfo[wIndex];
		m_cbExamble3.AddString(str_SN);
	}
	
	PDArray1.CloseDevice();*/
	
	HTREEITEM hParent, hChild;
	WORD wCount;
	WORD wIndex;
	CString cListInfo[256];
	CPDArry PDArray1;
	CString str_SN;
	//hParent = m_tree.InsertItem(_T("PD Array SN"), TVI_ROOT);
	PDArray1.CloseDevice();
	PDArray1.ShowAllDevice(&wCount, cListInfo);
	for (wIndex = 0; wIndex < wCount; wIndex++)
	{
		str_SN = cListInfo[wIndex];
		hChild = m_tree.InsertItem(_T(str_SN), TVI_ROOT);
	}
	//m_tree.Expand(hParent, TVE_EXPAND);
	PDArray1.CloseDevice();
}


void CMFCApplication1Dlg::OnNMCustomdrawList1(NMHDR *pNMHDR, LRESULT *pResult)
{
	LPNMTVCUSTOMDRAW pNMCD = reinterpret_cast<LPNMTVCUSTOMDRAW>(pNMHDR);
	NMCUSTOMDRAW nmCustomDraw = pNMCD->nmcd;
	switch (nmCustomDraw.dwDrawStage)
	{
	case CDDS_ITEMPREPAINT:
	{
		if (COLOR_BLUE== nmCustomDraw.lItemlParam)
		{
			pNMCD->clrTextBk = RGB(51, 153, 255);
			pNMCD->clrText = RGB(255, 255, 255);
		}
		else if (COLOR_RED == nmCustomDraw.lItemlParam)
		{
			pNMCD->clrTextBk = RGB(255, 0, 0);		//背景颜色
			pNMCD->clrText = RGB(255, 255, 255);		//文字颜色
		}
		else if (COLOR_DEFAULT== nmCustomDraw.lItemlParam)
		{
			pNMCD->clrTextBk = RGB(255, 255, 255);
			pNMCD->clrText = RGB(0, 0, 0);
		}
		else
		{
			//
		}
		break;
	}
	default:
	{
		break;
	}
	}

	*pResult = 0;
	*pResult |= CDRF_NOTIFYPOSTPAINT;		//必须有，不然就没有效果
	*pResult |= CDRF_NOTIFYITEMDRAW;		//必须有，不然就没有效果
	return;
}


void CMFCApplication1Dlg::OnBnClickedButton2()
{
	Test_Terminate_flag = TRUE;//用来终止start测试
	

	/*int i = 0;
	while (1)
	{
		i++;
		Sleep(2000);
		MSG msg;
		if (PeekMessage(&msg, (HWND)NULL, 0, 0, PM_REMOVE))
		{
			::SendMessage(msg.hwnd, msg.message, msg.wParam, msg.lParam);
		}
	}*/
}


void CMFCApplication1Dlg::YieldToPeers()
{
	MSG	msg;
	while (::PeekMessage(&msg, NULL, 0, 0, PM_NOREMOVE))
	{
		if (!AfxGetThread()->PumpMessage())
			break;
	}
}

void CMFCApplication1Dlg::WM_init()
{
	CString strUDLConfigXMLFile;
	TCHAR m_tszAppFolder[1024];
	char cErrMsg[512];
	CString str1;
	GetCurrentDirectory(sizeof(m_tszAppFolder), m_tszAppFolder);
	strUDLConfigXMLFile.Format("%s\\config\\UDL_WM.xml", m_tszAppFolder);

	try
	{
		CoInitialize(NULL);
		ZeroMemory(cErrMsg, sizeof(cErrMsg));
		HRESULT hr = m_pEngine.CreateInstance(__uuidof(UDL2_Engine));
		if (hr == S_FALSE)
		{
			m_pEngine->GetLastErrorMessage(cErrMsg, sizeof(cErrMsg));
			MessageBox(_T("输入有误UDL2_Engine!"));
			return;
		}

		hr = m_pWM.CreateInstance(__uuidof(UDL2_WM));
		if (hr == S_FALSE)
		{
			m_pEngine->GetLastErrorMessage(cErrMsg, sizeof(cErrMsg));
			MessageBox(_T("输入有误UDL2_WM!"));
			return;
		}

		hr = m_pEngine->LoadConfiguration((_bstr_t)strUDLConfigXMLFile);
		if (hr == S_FALSE)
		{
			m_pEngine->GetLastErrorMessage(cErrMsg, sizeof(cErrMsg));
			MessageBox(_T("WM xml输入有误!"));
			return;
		}
	}
	catch (char* ptszErrorMsg)
	{
		MessageBox(_T("输入有误!"));
		return;
	}

	HRESULT hr = m_pEngine->OpenEngine();
	if (hr == S_FALSE)
	{
		MessageBox(_T("open engine输入有误!"));
		return;
	}
	
	
}