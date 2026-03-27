
// MFCApplication1Dlg.h : header file
//
#include "libusbk.h"

#pragma once


// CMFCApplication1Dlg dialog
class CMFCApplication1Dlg : public CDialogEx
{
// Construction
public:
	CMFCApplication1Dlg(CWnd* pParent = NULL);	// standard constructor

// Dialog Data
#ifdef AFX_DESIGN_TIME
	enum { IDD = IDD_MFCAPPLICATION1_DIALOG };
#endif

	protected:
	virtual void DoDataExchange(CDataExchange* pDX);	// DDX/DDV support


// Implementation
protected:
	HICON m_hIcon;

	// Generated message map functions
	virtual BOOL OnInitDialog();
	afx_msg void OnSysCommand(UINT nID, LPARAM lParam);
	afx_msg void OnPaint();
	afx_msg HCURSOR OnQueryDragIcon();
	DECLARE_MESSAGE_MAP()
public:
	afx_msg void OnBnClickedButton1();
	
	int test_min_set;
	CTreeCtrl m_tree;
	CComboBox m_cbExamble;

	CMFCButton button_color;
	CListCtrl m_listctrl;
	CListCtrl m_listctrl2;
	afx_msg void OnTvnSelchangedTree1();
	afx_msg void OnNMCustomdrawList1(NMHDR *pNMHDR, LRESULT *pResult);
	int PDArray_Num;
	int WMArray_Num;
	void YieldToPeers();
	afx_msg void OnBnClickedButton2();
	afx_msg void WM_init();
	
};
