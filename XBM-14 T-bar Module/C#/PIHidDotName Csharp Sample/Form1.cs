﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using PIEHid32Net;
using System.Security.Cryptography; //AES https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aes?view=net-8.0
using System.IO; //AES

namespace PIHidDotName_Csharp_Sample
{
    public partial class Form1 : Form, PIEDataHandler, PIEErrorHandler
    {
        PIEDevice[] devices;
        
        int[] cbotodevice=null; //for each item in the CboDevice list maps this index to the device index.  Max devices =100 
        byte[] wData = null; //write data buffer
        int selecteddevice=-1; //set to the index of CboDevice
        long saveabsolutetime;  //for timestamp demo
       
        //for thread-safe way to call a Windows Forms control
        // This delegate enables asynchronous calls for setting
        // the text property on a TextBox control.
        delegate void SetTextCallback(string text);
        delegate void SetClearCallback();
        Control c;
        ListBox thisListBox;
        //end thread-safe

        byte[] lastdata = null;

        //AES
        Aes myAes;
        byte[] myKey;
        byte[] myIV;

        public Form1()
        {
            InitializeComponent();
            //BtnEnumerate_Click(this, null);

            //AES
            myAes = Aes.Create(); //creates object with Key and IV
            myAes.Mode = CipherMode.CBC; //Must match X-keys mode which is CBC
            myAes.Padding = PaddingMode.Zeros; //Must match X-keys which is Zeros
            myAes.KeySize = 128; //Must match X-keys which is 16 byte AES key

            myKey = new byte[16];
            myIV = new byte[16];
        }

        //data callback    
        public void HandlePIEHidData(Byte[] data, PIEDevice sourceDevice, int error)
        {
            //check the sourceDevice and make sure it is the same device as selected in CboDevice   
            if (sourceDevice == devices[selecteddevice])
            {
                //write raw data to listbox1 in HEX
                String output = "Callback: " + sourceDevice.Pid + ", ID: " + selecteddevice.ToString() + ", data=";
                for (int i = 0; i < sourceDevice.ReadLength; i++)
                {
                    output = output + BinToHex(data[i]) + " ";
                }
                
                thisListBox = listBox1;
                this.SetListBox(output);

                if (data[2] < 4) //General Incoming Data
                {
                    //read the unit ID
                    c = this.LblUnitID;
                    this.SetText(data[1].ToString());

                    //check the keyboard state byte 
                    byte val2;
                    val2 = (byte)(data[6] & 1);
                    if (val2 == 0)
                    {
                        c = this.LblNumLk;
                        this.SetText("NumLock: off");
                    }
                    else
                    {
                        c = this.LblNumLk;
                        this.SetText("NumLock: on");
                    }
                    val2 = (byte)(data[6] & 2);
                    if (val2 == 0)
                    {
                        c = this.LblCapsLk;
                        this.SetText("CapsLock: off");
                    }
                    else
                    {
                        c = this.LblCapsLk;
                        this.SetText("CapsLock: on");
                    }
                    val2 = (byte)(data[6] & 4);
                    if (val2 == 0)
                    {
                        c = this.LblScrLk;
                        this.SetText("ScrLock: off");
                    }
                    else
                    {
                        c = this.LblScrLk;
                        this.SetText("ScrLock: on");
                    }

                    //gpio inputs
                    val2 = (byte)(data[6] & 16);
                    if (val2 == 0)
                    {
                        c = this.lblPin1;
                        this.SetText("GPIO pin 1: off");
                    }
                    else
                    {
                        c = this.lblPin1;
                        this.SetText("GPIO pin 1: on");
                    }
                    val2 = (byte)(data[6] & 32);
                    if (val2 == 0)
                    {
                        c = this.lblPin2;
                        this.SetText("GPIO pin 2: off");
                    }
                    else
                    {
                        c = this.lblPin2;
                        this.SetText("GPIO pin 2: on");
                    }
                    val2 = (byte)(data[6] & 64);
                    if (val2 == 0)
                    {
                        c = this.lblPin3;
                        this.SetText("GPIO pin 3: off");
                    }
                    else
                    {
                        c = this.lblPin3;
                        this.SetText("GPIO pin 3: on");
                    }
                    val2 = (byte)(data[6] & 128);
                    if (val2 == 0)
                    {
                        c = this.lblPin4;
                        this.SetText("GPIO pin 4: off");
                    }
                    else
                    {
                        c = this.lblPin4;
                        this.SetText("GPIO pin 4: on");
                    }

                    //Buttons
                    //this routine is for separating out the individual button presses/releases from the data byte array.
                    int maxcols = 3; //number of columns of Xkeys digital button data, labeled "Keys" in P.I. Engineering SDK - General Incoming Data Input Report
                    int maxrows = 8; //constant, 8 bits per byte
                    c = this.lblButtons;
                    string buttonsdown = "Buttons: "; //for demonstration, reset this every time a new input report received
                    this.SetText(buttonsdown);
                    for (int i = 0; i < maxcols; i++) //loop through digital button bytes 
                    {
                        for (int j = 0; j < maxrows; j++) //loop through each bit in the button byte
                        {
                            int temp1 = (int)Math.Pow(2, j); //1, 2, 4, 8, 16, 32, 64, 128
                            int bitkeynum = 8 * i + j; 
                            byte temp2 = (byte)(data[i + 3] & temp1); //check using bitwise AND the current value of this bit. The + 3 is because the 1st button byte starts 3 bytes in at data[3]
                            byte temp3 = (byte)(lastdata[i + 3] & temp1); //check using bitwise AND the previous value of this bit
                            int state = 0; //0=was up, now up, 1=was up, now down, 2= was down, still down, 3= was down, now up
                            if (temp2 != 0 && temp3 == 0) state = 1; //press
                            else if (temp2 != 0 && temp3 != 0) state = 2; //held down
                            else if (temp2 == 0 && temp3 != 0) state = 3; //release
                            int[] keynumbermap = new int[24]; //map the bitwise keynum to index given in SDK documentation
                            //Column 1
                            keynumbermap[0] = 0; keynumbermap[1] = 1; keynumbermap[2] = 2; keynumbermap[3] = 3; keynumbermap[4] = 4; keynumbermap[5] = 5;
                            //Column 2
                            keynumbermap[12] = 6; keynumbermap[13] = 7;
                            //Column 3
                            keynumbermap[16] = 8; keynumbermap[17] = 9; keynumbermap[18] = 10; keynumbermap[19] = 11; keynumbermap[20] = 12; keynumbermap[21] = 13;

                            int keynum = keynumbermap[bitkeynum];
                            switch (state)
                            {
                                case 1: //key was up and now is pressed
                                    buttonsdown = buttonsdown + keynum.ToString() + " ";
                                    c = this.lblButtons;
                                    SetText(buttonsdown);
                                    break;
                                case 2: //key was pressed and still is pressed
                                    buttonsdown = buttonsdown + keynum.ToString() + " ";
                                    c = this.lblButtons;
                                    SetText(buttonsdown);
                                    break;
                                case 3: //key was pressed and now released
                                    break;
                            }
                            //or perform action based on key number
                            switch (keynum)
                            {
                                //Column 1
                                case 0: //button 0 (top left)
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 1: //button 1
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 2: //button 2
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 3: //button 3
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 4: //button 4
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 5: //button 5
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                //Column 2
                                case 6: //button 6
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 7: //button 7
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                //Column 3
                                case 8: //button 8
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 9: //button 9
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 10: //button 10
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 11: //button 11
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 12: //button 12
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 13: //button 13
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                            }
                        }
                    }

                    //Virtual buttons are in data[7]
                    maxcols = 1; //1 byte of virtual buttons
                    maxrows = 8; //number of rows of Xkeys digital button data
                    int virtualbuttonsbyteoffset = 7; //Virtual buttons are in data[7]
                    c = this.lblVirtualButtons;
                    buttonsdown = "Virtual Buttons: "; //for demonstration, reset this every time a new input report received
                    this.SetText(buttonsdown);

                    for (int i = 0; i < maxcols; i++) //loop through digital button bytes 
                    {
                        for (int j = 0; j < maxrows; j++) //loop through each bit in the button byte
                        {
                            int temp1 = (int)Math.Pow(2, j); //1, 2, 4, 8, 16, 32, 64, 128
                            int thisrow = j; //bit in byte 0, 1, 2, 3, 4, 5, 6, 7 
                            byte temp2 = (byte)(data[i + virtualbuttonsbyteoffset] & temp1); //check using bitwise AND the current value of this bit. The + 3 is because the 1st button byte starts 3 bytes in at data[3]
                            byte temp3 = (byte)(lastdata[i + virtualbuttonsbyteoffset] & temp1); //check using bitwise AND the previous value of this bit
                            int state = 0; //0=was up, now up, 1=was up, now down, 2= was down, still down, 3= was down, now up
                            if (temp2 != 0 && temp3 == 0) state = 1; //press
                            else if (temp2 != 0 && temp3 != 0) state = 2; //held down
                            else if (temp2 == 0 && temp3 != 0) state = 3; //release

                            switch (state)
                            {
                                case 1: //key was up and now is pressed
                                    buttonsdown = buttonsdown + (thisrow + 1).ToString() + " ";
                                    c = this.lblVirtualButtons;
                                    SetText(buttonsdown);
                                    break;
                                case 2: //key was pressed and still is pressed
                                    buttonsdown = buttonsdown + (thisrow + 1).ToString() + " ";
                                    c = this.lblVirtualButtons;
                                    SetText(buttonsdown);
                                    break;
                                case 3: //key was pressed and now released
                                    break;
                            }
                            switch (thisrow)
                            {
                                case 0: //virtual button 1
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 1: //virtual button 2
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 2: //virtual button 3
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 3: //virtual button 4
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 4: //virtual button 5
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 5: //virtual button 6
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 6: //virtual button 7
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 7: //virtual button 8
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                            }
                        }
                    }
                    //end virtual buttons

                    for (int i = 0; i < sourceDevice.ReadLength; i++)
                    {
                        lastdata[i] = data[i];
                    }
                    //end Buttons

                    //Tbar
                    c = this.LblTbar;
                    this.SetText("T-bar: " + data[9].ToString());

                    //time stamp info 4 bytes
                    long absolutetime = 16777216 * data[sourceDevice.ReadLength - 5] + 65536 * data[sourceDevice.ReadLength - 4] + 256 * data[sourceDevice.ReadLength - 3] + data[sourceDevice.ReadLength - 2];  //ms
                    long absolutetime2 = absolutetime / 1000; //seconds
                    c = this.lblAbsTime;
                    this.SetText("absolute time: " + absolutetime2.ToString() + " s");
                    long deltatime = absolutetime - saveabsolutetime;
                    c = this.lblDeltaTime;
                    this.SetText("delta time: " + deltatime + " ms");
                    saveabsolutetime = absolutetime;
                } //end of if data[2]<4
                else if (data[2] == 167) //0xA7 backlight LED state request
                {
                    thisListBox = listBox3;
                    this.ClearListBox();
                    this.SetListBox("Button=" + (data[3]));
                    //bank 1
                    this.SetListBox("Bank 1 Red=" + (data[4]));
                    this.SetListBox("Bank 1 Green=" + (data[5]));
                    this.SetListBox("Bank 1 Blue=" + (data[6]));
                    if (data[10] == 1) //0=no flash, 1=flashing
                        this.SetListBox("Flash Bank 1 = flashing");
                    else this.SetListBox("Flash Bank 1 = not flashing"); 
                    //bank 2
                    this.SetListBox("Bank 2 Red=" + (data[7]));
                    this.SetListBox("Bank 2 Green=" + (data[8]));
                    this.SetListBox("Bank 2 Blue=" + (data[9]));
                    if (data[11] == 1)
                        this.SetListBox("Flash Bank 2 = flashing");
                    else this.SetListBox("Flash Bank 2 = not flashing"); 
                    this.SetListBox("Flash frequency=" + (data[13])); 
                }
                else if (data[2] == 0x8B) //encrypt results
                {
                    c = lblXkeysEncrypt;
                    string encryptedbytes = "";
                    for (int j = 0; j < 32; j++)
                    {
                        encryptedbytes = encryptedbytes + BinToHex(data[3 + j]) + ", ";
                    }
                    SetText(encryptedbytes);
                }
                else if (data[2] == 0x8C) //decrypt results
                {
                    c = lblXkeysDecrypt;
                    string decryptedbytes = "";
                    for (int j = 0; j < 32; j++)
                    {
                        if (data[3 + j] != 0)
                        {
                            decryptedbytes = decryptedbytes + (char)(data[3 + j]);
                        }
                    }
                    SetText(decryptedbytes);
                }
            }
        }
        //error callback
        public void HandlePIEHidError(PIEDevice sourceDevice, Int32 error)
        {
            this.SetToolStrip("Error: " + error.ToString());
        }
        //for threadsafe setting of Windows Forms control
        private void SetText(string text)
        {
            // InvokeRequired required compares the thread ID of the
            // calling thread to the thread ID of the creating thread.
            // If these threads are different, it returns true.
            if (this.c.InvokeRequired)
            {
                SetTextCallback d = new SetTextCallback(SetText);
                this.Invoke(d, new object[] { text });
            }
            else
            {
                this.c.Text = text;
            }
        }
        //for threadsafe setting of Windows Forms control
        private void SetListBox(string text)
        {
            // InvokeRequired required compares the thread ID of the
            // calling thread to the thread ID of the creating thread.
            // If these threads are different, it returns true.
            if (this.thisListBox.InvokeRequired)
            {
                SetTextCallback d = new SetTextCallback(SetListBox);
                this.Invoke(d, new object[] { text });
            }
            else
            {
                this.thisListBox.Items.Add(text);
                this.thisListBox.SelectedIndex = this.thisListBox.Items.Count - 1;
            }
        }
        //for threadsafe setting of Windows Forms control
        private void ClearListBox()
        {
            // InvokeRequired required compares the thread ID of the
            // calling thread to the thread ID of the creating thread.
            // If these threads are different, it returns true.
            if (this.thisListBox.InvokeRequired)
            {
                SetClearCallback d = new SetClearCallback(ClearListBox);
                this.Invoke(d, new object[] { });
            }
            else
            {
                this.thisListBox.Items.Clear();
            }
        }
        //for threadsafe setting of Windows Forms control
        private void SetToolStrip(string text)
        {
            // InvokeRequired required compares the thread ID of the
            // calling thread to the thread ID of the creating thread.
            // If these threads are different, it returns true.
            if (this.statusStrip1.InvokeRequired)
            {
                SetTextCallback d = new SetTextCallback(SetToolStrip);
                this.Invoke(d, new object[] { text });
            }
            else
            {
                this.toolStripStatusLabel1.Text = text;
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            toolStripStatusLabel1.Text = "";
            cboIndex.SelectedIndex = 0;
            cboBankLegacy.SelectedIndex = 0;
            cboKeyIndexGet.SelectedIndex = 0;
            cboBank.SelectedIndex = 0;
            cboColor.SelectedIndex = 1;
            cboBL.SelectedIndex = 0;
            
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            //closeinterfaces on all devices that have been setup (SetupInterface called)
            for (int i = 0; i < cboDevices.Items.Count; i++)
            {
                devices[cbotodevice[i]].CloseInterface();
            }
            System.Environment.Exit(0);
        }

        private void btnEnumerate_Click(object sender, EventArgs e)
        {
            cboDevices.Items.Clear();
            cbotodevice = new int[128]; //128=max # of devices
            //enumerate and setupinterfaces for all devices
            devices = PIEHid32Net.PIEDevice.EnumeratePIE();
            if (devices.Length == 0)
            {
                toolStripStatusLabel1.Text = "No Devices Found";
            }
            else
            {
                //System.Media.SystemSounds.Beep.Play(); 
                int cbocount = 0; //keeps track of how many valid devices were added to the CboDevice box
                for (int i = 0; i < devices.Length; i++)
                {
                    //information about device
                    //PID = devices[i].Pid);
                    //HID Usage = devices[i].HidUsage);
                    //HID Usage Page = devices[i].HidUsagePage);
                    //HID Version = devices[i].Version); //NOTE: this is NOT the firmware version which is given in the descriptor
                    int hidusagepg = devices[i].HidUsagePage;
                    int hidusage = devices[i].HidUsage;
                    if (devices[i].HidUsagePage == 0xc && devices[i].WriteLength > 1)
                    {
                        switch (devices[i].Pid)
                        {
                            case 1396:
                                //Device 1 Keyboard, Multimedia, Input and Output endpoints, PID #1
                                cboDevices.Items.Add(devices[i].ProductString + " (" + devices[i].Pid + "=PID #1)");
                                cbotodevice[cbocount] = i;
                                cbocount++;
                                cboPIDs.SelectedIndex = 0;
                                break;
                            case 1397:
                                //Device 2 Keyboard (boot), Multimedia, Input and Output endpoints, PID #2
                                cboDevices.Items.Add(devices[i].ProductString + " (" + devices[i].Pid + "=PID #2)");
                                cbotodevice[cbocount] = i;
                                cbocount++;
                                cboPIDs.SelectedIndex = 1;
                                break;
                            case 1398:
                                //Device 3 Keyboard, Joystick, Input and Output endpoints, PID #3
                                cboDevices.Items.Add(devices[i].ProductString + " (" + devices[i].Pid + "=PID #3)");
                                cbotodevice[cbocount] = i;
                                cbocount++;
                                cboPIDs.SelectedIndex = 2;
                                break;
                            case 1399:
                                //Device 4 Mouse, Joystick, Input and Output endpoints, PID #4
                                cboDevices.Items.Add(devices[i].ProductString + " (" + devices[i].Pid + "=PID #4)");
                                cbotodevice[cbocount] = i;
                                cbocount++;
                                cboPIDs.SelectedIndex = 3;
                                break;
                            case 1400:
                                //Device 5 Keyboard (boot), Mouse (boot), Input and Output endpoints, PID #5
                                cboDevices.Items.Add(devices[i].ProductString + " (" + devices[i].Pid + "=PID #5)");
                                cbotodevice[cbocount] = i;
                                cbocount++;
                                cboPIDs.SelectedIndex = 4;
                                break;
                            case 1401:
                                //Device 6 Input and Output endpoints, PID #6
                                cboDevices.Items.Add(devices[i].ProductString + " (" + devices[i].Pid + "=PID #6)");
                                cbotodevice[cbocount] = i;
                                cbocount++;
                                cboPIDs.SelectedIndex = 5;
                                break;
                            case 1402:
                                //Device 7 Keyboard, Joystick, Mouse, Multimedia, Input and Output endpoints, PID #7
                                cboDevices.Items.Add(devices[i].ProductString + " (" + devices[i].Pid + "=PID #7)");
                                cbotodevice[cbocount] = i;
                                cbocount++;
                                cboPIDs.SelectedIndex = 6;
                                break;
                            default:
                                cboDevices.Items.Add("Unknown Device: " + devices[i].ProductString + " (" + devices[i].Pid + ")");
                                cbotodevice[cbocount] = i;
                                cbocount++;
                                cboPIDs.SelectedIndex = 0;
                                break;
                        }
                        devices[i].SetupInterface();
                        devices[i].suppressDuplicateReports = false;
                        EnableAllControls();
                    }
                    else
                    {
                        if (devices[i].Pid == 1292) //if see this pid, contact tech support: tech@piengineering.com
                        {
                            cboDevices.Items.Add("Bootload device: " + devices[i].ProductString + " (" + devices[i].Pid + ")");
                            cbotodevice[cbocount] = i;
                            cbocount++;
                            DisableAllControls(devices[i].Pid);
                            MessageBox.Show("Device in bootloader mode. Contact P.I. Engineering.");
                        }
                        else if (devices[i].Pid == 1403)
                        {
                            //Device 8 Keyboard only endpoint
                            cboDevices.Items.Add(devices[i].ProductString + " (" + devices[i].Pid + "=PID #8), ID: " + i);
                            cbotodevice[cbocount] = i;
                            cbocount++;
                            cboPIDs.SelectedIndex = 7;
                            DisableAllControls(devices[i].Pid);
                            MessageBox.Show("Device in KVM PID, no input or output reports are available. To exit KVM mode, replug device into usb port and immediately after press Scroll Lock 10-15 times.");
                        }
                    }
                }
            }
            if (cboDevices.Items.Count > 0)
            {
                cboDevices.SelectedIndex = 0;
                selecteddevice = cbotodevice[cboDevices.SelectedIndex];
                wData = new byte[devices[selecteddevice].WriteLength];//go ahead and setup for write
                lastdata = new byte[devices[selecteddevice].ReadLength];
                lblVersion.Text = devices[selecteddevice].Version.ToString();
                lblSiliconGeneratedID.Text = devices[selecteddevice].SerialNumberString;
                toolStripStatusLabel1.Text = devices[selecteddevice].ProductString + " found";
            }

        }

        private void DisableAllControls(int thispid)
        {
            foreach (Control cl in Controls)
            {
                if (cl.Name != "btnEnumerate")
                {
                    cl.Enabled = false;
                }
            }
        }

        private void EnableAllControls()
        {
            foreach (Control cl in Controls)
            {
                if (cl.Name != "BtnEnumerate")
                {
                    cl.Enabled = true;
                }
            }
        }

        private void cboDevices_SelectedIndexChanged(object sender, EventArgs e)
        {
            selecteddevice = cbotodevice[cboDevices.SelectedIndex];
            wData = new byte[devices[selecteddevice].WriteLength];//size write array 
            lastdata = new byte[devices[selecteddevice].ReadLength];
        }

        private void btnCallback_Click(object sender, EventArgs e)
        {
            //setup callback if there are devices found for each device found

            if (selecteddevice != -1)
            {
                for (int i = 0; i < cboDevices.Items.Count; i++)
                {
                    //use the cbotodevice array which contains the mapping of the devices in the CboDevices to the actual device IDs
                    devices[cbotodevice[i]].SetErrorCallback(this);
                    devices[cbotodevice[i]].SetDataCallback(this);
                    devices[cbotodevice[i]].callNever = false;
                }
                toolStripStatusLabel1.Text = "Callback on";
            }
        }

        private void chkSuppress_CheckedChanged(object sender, EventArgs e)
        {
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                if (chkSuppress.Checked == false)
                {
                    devices[selecteddevice].suppressDuplicateReports = false;
                }
                else
                {
                    devices[selecteddevice].suppressDuplicateReports = true;
                }
            }
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            listBox1.Items.Clear();

            for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
            {
                wData[j] = 0;
            }
            wData[0] = 0;
            wData[1] = 214; //0xD6
            int result = 404;
            while (result == 404) { result = devices[selecteddevice].WriteData(wData); }

            wData[0] = 0;
            wData[1] = 177; 
            result = 404;
            while (result == 404) { result = devices[selecteddevice].WriteData(wData); }

            wData[0] = 0;
            wData[1] = 165;
            wData[4] = 126;
            wData[5] = 65;
            wData[6] = 254;
            for (int j = 0; j < 2; j++)
            {
                wData[3] = (byte)j;
                for (int i = 0; i < 14; i++)
                {
                    wData[2] = (byte)i;
                    result = 404;
                    while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                }
            }

           
        }

        private void chkLED1_CheckStateChanged(object sender, EventArgs e)
        {
            //Write to GPIO outputs
            if (selecteddevice != -1)
            {
                CheckBox thischkbox = (CheckBox)sender;
                byte ledindex = Convert.ToByte(thischkbox.Tag.ToString());

                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[1] = 179; //0xB3
                wData[2] = ledindex; //1=Pin 1, 2=Pin 2, 3=Pin 3, 4=Pin 4
                wData[3] = (byte)thischkbox.CheckState; //0=off, 1=on, 2=flash

                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Set LED";
                }
            }
        }

        private void btnUnitID_Click(object sender, EventArgs e)
        {
            //Write Unit ID to the device
            if (selecteddevice != -1) //do nothing if not enumerated
            {

                //write Unit ID given in the TxtSetUnitID box
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[0] = 0;
                wData[1] = 189; //0xBD
                wData[2] = (byte)(Convert.ToInt16(TxtSetUnitID.Text));

                int result=404;
				
				while(result==404){result = devices[selecteddevice].WriteData(wData);}
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Write Unit ID";
                }
            }
        }

        //RGB Backlight Features
        private void cboColor_SelectedIndexChanged(object sender, EventArgs e)
        {

            switch (cboColor.SelectedIndex)
            {
                case 0: //off
                    txtR.Text = "0";
                    txtG.Text = "0";
                    txtB.Text = "0";
                    break;
                case 1: //red
                    txtR.Text = "255";
                    txtG.Text = "0";
                    txtB.Text = "0";
                    break;
                case 2: //orange
                    txtR.Text = "255";
                    txtG.Text = "20";
                    txtB.Text = "0";
                    break;
                case 3: //yellow
                    txtR.Text = "255";
                    txtG.Text = "129";
                    txtB.Text = "0";
                    break;
                case 4: //green
                    txtR.Text = "0";
                    txtG.Text = "255";
                    txtB.Text = "0";
                    break;
                case 5: //turquoise
                    txtR.Text = "0";
                    txtG.Text = "255";
                    txtB.Text = "129";
                    break;
                case 6: //blue
                    txtR.Text = "0";
                    txtG.Text = "0";
                    txtB.Text = "255";
                    break;
                case 7: //pink
                    txtR.Text = "255";
                    txtG.Text = "8";
                    txtB.Text = "40";
                    break;
                case 8: //purple
                    txtR.Text = "150";
                    txtG.Text = "0";
                    txtB.Text = "255";
                    break;
                case 9: //white
                    txtR.Text = "255";
                    txtG.Text = "255";
                    txtB.Text = "255";
                    break;
            }
        }

        private void btnSetRGB_Click(object sender, EventArgs e)
        {
            //Set individual led 
            //Index (in decimal)
            //Columns-->
            //  0   -  8
            //  1   -  9
            //  2   -  10 
            //  3   -  11  
            //  4   6  12 
            //  5   7  13   
            //Upper LEDs are bank 1, bankindex = 0
            //Lower LEDs are bank 2, bankindex = 1


            if (selecteddevice != -1)
            {
                byte index = Convert.ToByte(cboIndex.Text);
                byte bank = (byte)cboBank.SelectedIndex;
                int checkstate = (int)chkRGBFlash.CheckState;
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                int result = 0;
                if ((bank == 0) || (bank == 1))
                {
                    wData[1] = 165; //0xA5
                    wData[2] = index;
                    wData[3] = bank; //0=bank 1 (top), 1=bank 2 (bottom)
                    wData[4] = Convert.ToByte(txtR.Text);
                    wData[5] = Convert.ToByte(txtG.Text);
                    wData[6] = Convert.ToByte(txtB.Text);
                    wData[7] = (byte)chkRGBFlash.CheckState; //0=no flash, 1=flash

                    result = 404;
                    while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                    if (result != 0)
                    {
                        toolStripStatusLabel1.Text = "Write Fail: " + result;
                    }
                    else
                    {
                        toolStripStatusLabel1.Text = "Write Success - RGB LED";
                    }
                }
                else if (bank == 2) //do both
                {
                    wData[1] = 165; //0xA5
                    wData[2] = index;
                    wData[3] = 0; //0=bank 1 (top), 1=bank 2 (bottom)
                    wData[4] = Convert.ToByte(txtR.Text);
                    wData[5] = Convert.ToByte(txtG.Text);
                    wData[6] = Convert.ToByte(txtB.Text);
                    wData[7] = (byte)chkRGBFlash.CheckState; //0=no flash, 1=flash

                    result = 404;
                    while (result == 404) { result = devices[selecteddevice].WriteData(wData); }

                    wData[1] = 165; //0xA5
                    wData[2] = index;
                    wData[3] = 1; //0=bank 1 (top), 1=bank 2 (bottom)
                    wData[4] = Convert.ToByte(txtR.Text);
                    wData[5] = Convert.ToByte(txtG.Text);
                    wData[6] = Convert.ToByte(txtB.Text);
                    wData[7] = (byte)chkRGBFlash.CheckState; //0=no flash, 1=flash

                    result = 404;
                    while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                    if (result != 0)
                    {
                        toolStripStatusLabel1.Text = "Write Fail: " + result;
                    }
                    else
                    {
                        toolStripStatusLabel1.Text = "Write Success - RGB LED";
                    }
                }
            }
        }

        private void btnBoth_Click(object sender, EventArgs e)
        {
            
        }

        private void btnSetAllBank1_Click(object sender, EventArgs e)
        {
            if (selecteddevice != -1)
            {
                int result = 0;

                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[1] = 166;//0xA6
                wData[2] = 0; //0=Bank 1 (upper LEDs), 1=Bank 2 (lower LEDs)
                wData[3] = Convert.ToByte(txtR.Text);
                wData[4] = Convert.ToByte(txtG.Text);
                wData[5] = Convert.ToByte(txtB.Text);

                result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }


                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Set All Bank 1 RGB LED";
                }
            }
        }

        private void btnSetAllBank2_Click(object sender, EventArgs e)
        {
            if (selecteddevice != -1)
            {
                int result = 0;

                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[1] = 166; //0xA6
                wData[2] = 1; //0=Bank 1 (upper LEDs), 1=Bank 2 (lower LEDs)
                wData[3] = Convert.ToByte(txtR.Text);
                wData[4] = Convert.ToByte(txtG.Text);
                wData[5] = Convert.ToByte(txtB.Text);

                result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }


                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Set All Bank 2 RGB LED";
                }
            }
        }

        private void btnRGBIntensity_Click(object sender, EventArgs e)
        {
            //RGB global (per bank) dimming control - this command is meant to be used once the banks of LEDs have been configured to the desired colors. 255 is the brightest or 100% on and 0 is the dimmest.
            //Use caution if set a dim factor to 0, LEDs will be off and will not be able to be turned on without changing the dim factor to a non-0 value.
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[0] = 0;
                wData[1] = 164; //0xA4
                wData[2] = (byte)(Convert.ToInt16(txtBank1.Text)); //1-255 for brightness of bank 1 bl leds
                wData[3] = (byte)(Convert.ToInt16(txtBank2.Text)); //1-255 for brightness of bank 2 bl leds

                int result = 404;
                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }

                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Dim Factors";
                }
            }
        }

        //Legacy Backlight Features
        private void chkBLOnOff_CheckStateChanged(object sender, EventArgs e)
        {
            //This command is a legacy command meant to mimick the Set Individual Backlight command on non-RGB LED X-keys devices.
            //Using the legacy backlight commands will force the upper bank of LEDs to be blue and the lower to be red

            //Use the Set Flash Freq to control frequency of blink
            //Index (in decimal)
            //Columns-->
            //Upper LEDs (ie Bank 1)
            //  0   -  8
            //  1   -  9
            //  2   -  10 
            //  3   -  11  
            //  4   6  12 
            //  5   7  13 
            //Lower LEDs (ie Bank 2)
            //  14   -   22
            //  15   -   23
            //  16   -   24 
            //  17   -   25  
            //  18   20  26 
            //  19   21  27

            if (selecteddevice != -1)
            {
                //first get selected index
                string sindex = cboBL.Text;
                int bank = cboBankLegacy.SelectedIndex;
                int iindex = cboBL.SelectedIndex;
                if (bank == 1) iindex = iindex + 14;
                
                //now get state
                int state = (int)chkBLOnOff.CheckState;

                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[1] = 181; //0xB5
                wData[2] = (byte)(iindex); //Index
                wData[3] = (byte)state; //0=off, 1=on, 2=flash

                int result = 404;
                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }

                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Legacy Backlight";
                }
            }
        }

        private void chkBank1OnOff_CheckedChanged(object sender, EventArgs e)
        {
            //This command is a legacy command meant to mimick the All Bank X On/Off command on non-RGB LED X-keys devices.
            //Turns on or off all Bank 1 (upper LEDs) using current intensity. If on color is blue.
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                byte sl = 0;

                if (chkBank1OnOff.Checked == true) sl = 255;
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[0] = 0;
                wData[1] = 182; //0xB6
                wData[2] = 0;  //0 for bank1, 1 for bank2
                wData[3] = (byte)sl; //OR turn individual rows on or off using bits.  1st bit=row 1, 2nd bit=row 2, 3rd bit =row 3, etc

                int result = 404;
                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }

                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success-Legacy All Bank 1 Backlight on/off";
                }
            }
        }

        private void chkBank2OnOff_CheckedChanged(object sender, EventArgs e)
        {
            //This command is a legacy command meant to mimick the All Bank X On/Off command on non-RGB LED X-keys devices.
            //Turns on or off all Bank 2 (lower LEDs) using current intensity. If on color is red.
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                byte sl = 0;

                if (chkBank2OnOff.Checked == true) sl = 255;
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[0] = 0;
                wData[1] = 182; //0xB6
                wData[2] = 1;  //0 for bank 1, 1 for bank 2
                wData[3] = (byte)sl; //OR turn individual rows on or off using bits.  1st bit=row 1, 2nd bit=row 2, 3rd bit =row 3, etc

                int result = 404;
                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }

                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success-Legacy All Bank 2 Backlight on/off";
                }
            }
        }

        private void btnLegacyIntensity_Click(object sender, EventArgs e)
        {
            //This command is a legacy command meant to mimick the Set Intensity command on non-RGB LED X-keys devices.
            //Same as "Dim Factor" 
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[0] = 0;
                wData[1] = 187; //0xBB
                wData[2] = (byte)(Convert.ToInt16(txtIntensity.Text)); //0-255 for brightness of bank 1 bl leds
                wData[3] = (byte)(Convert.ToInt16(txtIntensity2.Text)); //0-255 for brightness of bank 2 bl leds

                int result = 404;
                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }

                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success-Legacy Backlighting Intensity";
                }
                txtBank1.Text = txtIntensity.Text;
                txtBank2.Text = txtIntensity2.Text;
            }
        }

        //Global Backlight Features
        private void btnSetFlash_Click(object sender, EventArgs e)
        {
            //Sets the frequency of flashing for both the LEDs and backlighting
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[0] = 0;
                wData[1] = 180; // 0xB4
                wData[2] = (byte)(Convert.ToInt16(txtFlashFreq.Text));

                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Set Flash Frequency";
                }
            }
        }

        private void btnBLToggle_Click(object sender, EventArgs e)
        {
            //Sending this command toggles the backlights
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[0] = 0;
                wData[1] = 184; //0xB8

                int result=404;
				
				while(result==404){result = devices[selecteddevice].WriteData(wData);}
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Toggle BL";
                }
            }
        }

        private void btnSaveBL_Click(object sender, EventArgs e)
        {
            //Write current state of backlighting to EEPROM.  
            //NOTE: Is it not recommended to do this frequently as there are a finite number of writes to the EEPROM allowed
            if (selecteddevice != -1)
            {
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 199;  //0xC7
                wData[2] = 1; //anything other than 0 will save bl state to eeprom, default is 0
                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Save Backlight to EEPROM";
                }
            }
        }

        private void btnGetBLState_Click(object sender, EventArgs e)
        {
            //Sending this command will make the device return information about it, in this sample the returned information is
            //expected in the HandlePIEHidData callback routine. It is also possible to use the BlockingReadData command to 
            //get the returned data as demonstrated in BtnDescriptor_Click

            //Index (in decimal)
            //Columns-->
            //  0   -  8
            //  1   -  9
            //  2   -  10 
            //  3   -  11  
            //  4   6  12 
            //  5   7  13   
            //Upper LEDs are bank 1, bankindex = 0
            //Lower LEDs are bank 2, bankindex = 1

            if (selecteddevice != -1) //do nothing if not enumerated
            {

                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 167; //0xA7;
                wData[2] = (byte)(cboKeyIndexGet.SelectedIndex); //0=upper left key, 1, 
                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Get BL state";
                }
                //see HandlePIEHidData for handling of the returned data
            }
        }

        private void btnKBreflect_Click(object sender, EventArgs e)
        {
            //Sends native keyboard messages
            //Write some keys to the textbox, should be Abcd
            //send some hid codes to the textbox, these will be coming in on the native keyboard endpoint
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                int result;
                txtKeyboardReflector.Focus();
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 201; //0xC9

                wData[2] = 2;       //modifiers
                wData[3] = 0;       //always 0
                wData[4] = 0x04;    //hid code = a down
                wData[5] = 0;
                wData[6] = 0;
                wData[7] = 0;
                wData[8] = 0;
                wData[9] = 0;

                //use this method to ensure done writing data before executing the next write command
                result=404;
				while(result==404){result = devices[selecteddevice].WriteData(wData);}
                

                wData[2] = 0;       //modifiers
                wData[3] = 0;       //always 0
                wData[4] = 0;    //hid code = a up
                wData[5] = 0x05;    //hid code = b down
                wData[6] = 0x06;    //hid code = c down
                wData[7] = 0x07;    //hid code = d down
                wData[8] = 0;
                wData[9] = 0;

                result=404;
				while(result==404){result = devices[selecteddevice].WriteData(wData);}
                

                wData[2] = 0;
                wData[4] = 0;
                wData[5] = 0;  //b up
                wData[6] = 0;  //c up
                wData[7] = 0;  //d up
                wData[8] = 0;
                wData[9] = 0;

                result=404;
				while(result==404){result = devices[selecteddevice].WriteData(wData);}
                
            }
        }

        private void btnJoyreflect_Click(object sender, EventArgs e)
        {
            //Sends native joystick messages
            //Open up the game controller control panel to test these features, after clicking this button
            //go and make active the control panel properties and change will be seen
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                int result;
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 202;    //0xCA
                wData[2] = (byte)Math.Abs((Convert.ToByte(txtJoyX.Text) ^ 127) - 255);  //X, in raw form 0 to 127 from center to right, 255 to 128 from center to left but I like to use 0-255 where 0 is max left, 255 is max right
                wData[3] = (byte)(Convert.ToByte(txtJoyY.Text) ^ 127); //Y, raw data 0 to 127 from center down, 255 to 128 from center up, I convert so I can enter 0-255
                wData[4] = (byte)(Convert.ToByte(txtJoyZr.Text) ^ 127); //Z rotation, raw data 0 to 127 from center down, 255 to 128 from center up, I convert so I can enter 0-255
                wData[5] = (byte)(Convert.ToByte(txtJoyZ.Text) ^ 127); //Z, raw data 0 to 127 from center down, 255 to 128 from center up, I convert so I can enter 0-255
                wData[6] = (byte)(Convert.ToByte(txtJoySlider.Text) ^ 127); //Slider rotation, raw data 0 to 127 from center down, 255 to 128 from center up, I convert so I can enter 0-255

                wData[7] = Convert.ToByte(txtJoyButtons1.Text); //buttons 1-8, where bit 1 is button 1, bit 1 is button 2, etc.
                wData[8] = Convert.ToByte(txtJoyButtons2.Text); //buttons 9-16
                wData[9] = Convert.ToByte(txtJoyButtons3.Text); //buttons 17-24
                wData[10] = Convert.ToByte(txtJoyButtons4.Text); //buttons 25-32

                wData[11] = 0;

                wData[12] = Convert.ToByte(txtJoyHat.Text); //hat, where 0 is straight up, 1 is 45deg cw, etc and 8 is no hat
                result=404;
				while(result==404){result = devices[selecteddevice].WriteData(wData);}
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - joystick reflector";
                }
            }
        }

        private void btnMousereflect_Click(object sender, EventArgs e)
        {
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                wData[0] = 0;
                wData[1] = 203;    //0xcb
                wData[2] = Convert.ToByte(txtMouseButton.Text); //Buttons; 1=Left, 2=Right, 4=Center, 8=XButton1, 16=XButton2
                wData[3] = Convert.ToByte(txtMouseDx.Text); //Mouse X motion. 128=0 no motion, 1-127 is right, 255-129=left, finest inc (1 and 255) to coarsest (127 and 129).
                wData[4] = Convert.ToByte(txtMouseDy.Text); //Mouse Y motion. 128=0 no motion, 1-127 is down, 255-129=up, finest inc (1 and 255) to coarsest (127 and 129).
                wData[5] = Convert.ToByte(txtMouseWheel.Text);//Wheel Y. 128=0 no motion, 1-127 is up, 255-129=down, finest inc (1 and 255) to coarsest (127 and 129).
                devices[selecteddevice].WriteData(wData);

                //now send all 0s
                wData[0] = 0;
                wData[1] = 203; //0xCB
                wData[2] = 0; //buttons
                wData[3] = 0; //X
                wData[4] = 0; //Y
                wData[5] = 0; //wheel Y
                devices[selecteddevice].WriteData(wData);
            }
        }

        private void btnMultiMedia_Click(object sender, EventArgs e)
        {
            //Many multimedia commands require the app to have focus to work.  Some that don't are Mute (E2), Volume Increment (E9), Volume Decrement (EA)
            //The Multimedia reflector is mainly designed to be used as hardware mode macros.
            //Some common multimedia codes
            //Scan Next Track	00B5
            //Scan Previous Track	00B6
            //Stop	00B7
            //Play/Pause	00CD
            //Mute	00E2
            //Bass Boost	00E5
            //Loudness	00E7
            //Volume Up	00E9
            //Volume Down	00EA
            //Bass Up	0152
            //Bass Down	0153
            //Treble Up	0154
            //Treble Down	0155
            //Media Select	0183
            //Mail	018A
            //Calculator	0192
            //My Computer	0194
            //Search	0221
            //Home	0223
            //Back	0224
            //Forward	0225
            //Stop	0226
            //Refresh	0227
            //Favorites	022A

            if (selecteddevice != -1) //do nothing if not enumerated
            {
                int result = 0;
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[0] = 0;
                wData[1] = 225; //0xE1
                wData[2] = HexToBin(txtMMLow.Text); //Usage ID lo byte see hut1_12.pdf, pages 75-85 Consumer Page
                wData[3] = HexToBin(txtMMHigh.Text); //Usage ID hi byte see hut1_12.pdf, pages 75-85 Consumer Page

                result = 404;
                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }


                wData[0] = 0;
                wData[1] = 225; // 0xE1
                wData[2] = 0; //terminate
                wData[3] = 0; //terminate
                result = 404;
                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }


                //note when the "terminate" command is sent can sometimes have an effect on the behavior of the command
                //for example in volume decrement (EA=lo byte, 00=hi byte) if you send the terminate immediately after the e1 command it will
                //decrement the volume one step, if you send the e1 on the press and the terminate on the release the volume will continuously
                //decrement until the key is released.
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Write Multimedia";
                }
            }
        }

        private void btnMyComputer_Click(object sender, EventArgs e)
        {
            //Multimedia available on v30 firmware or above.
            if (selecteddevice != -1) //do nothing if not enumerated
            {

                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[0] = 0;
                wData[1] = 225; //0xE1
                wData[2] = HexToBin("94"); //Usage ID lo byte see hut1_12.pdf, pages 75-85 Consumer Page
                wData[3] = HexToBin("01"); //Usage ID hi byte see hut1_12.pdf, pages 75-85 Consumer Page

                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }

                wData[0] = 0;
                wData[1] = 225;  //0xE1
                wData[2] = 0; //terminate
                wData[3] = 0; //terminate
                result = 404;
                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }

                //note that when the "terminate" command is sent can sometimes have an effect on the behavior of the command
                //for example in volume decrement (EA=lo byte, 00=hi byte) if you send the terminate immediately after the e1 command it will
                //decrement the volume one step, if you send the e1 on the press and the terminate on the release the volume will continuously
                //decrement until the key is released.
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Write Multimedia";
                }
            }
        }

        private void btnSleep_Click(object sender, EventArgs e)
        {
            //Multimedia available on v30 firmware or above.
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[0] = 0;
                wData[1] = 226; //0xE2
                wData[2] = 2; //1=power down, 2=sleep, 4=wake up

                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                //NOTE this needs to be on the release of the key!!

                System.Threading.Thread.Sleep(1000); //this to simulate press/release

                wData[0] = 0;
                wData[1] = 226; //0xE2
                wData[2] = 0;

                result = 404;
                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }

                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Write Multimedia";
                }
            }
        }

        private void btnDescriptor_Click(object sender, EventArgs e)
        {
            //Sending the command will make the device return information about it
            if (selecteddevice != -1)
            {
                //IMPORTANT turn off the callback if going so data isn't grabbed there, turn it back on later
                bool savecallbackstate = devices[selecteddevice].callNever;
                devices[selecteddevice].callNever = true;

                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 214; //0xD6
                int result=404;
				
				while(result==404){result = devices[selecteddevice].WriteData(wData);}
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Descriptor, callback off";
                }
           
                byte[] data = null;
                int countout = 0;
                data = new byte[80];
                data[1] = 0;
                int ret = devices[selecteddevice].BlockingReadData(ref data, 100);
                while ((ret == 0 && data[2] != 214) || ret == 304)
                {
                    if (ret == 304)
                    {
                        // Didn't get any data for 100ms, increment the countout extra
                        countout += 99;
                    }
                    countout++;
                    if (countout > 1000) //increase this if have to check more than once
                        break;
                    ret = devices[selecteddevice].BlockingReadData(ref data, 100);
                }
                listBox2.Items.Clear();
                listBox2.Items.Add("PID " + (data[3] + 1).ToString());
                listBox2.Items.Add("Keymapstart=" + data[4].ToString());
                listBox2.Items.Add("Layer2offset=" + data[5].ToString());
                listBox2.Items.Add("SizeOfEEProm=" + (data[7] * 256 + data[6]).ToString());
                listBox2.Items.Add("MaxCol=" + data[8].ToString());
                listBox2.Items.Add("MaxRow=" + data[9].ToString());
                String pinson = "";
                if ((byte)(data[10] & 1) != 0) pinson = "NumLock, ";
                if ((byte)(data[10] & 2) != 0) pinson = pinson + "CapsLock, ";
                if ((byte)(data[10] & 4) != 0) pinson = pinson + "ScrLock, ";
                if ((byte)(data[10] & 16) != 0) pinson = pinson + "Pin 1, ";
                if ((byte)(data[10] & 32) != 0) pinson = pinson + "Pin 2, ";
                if ((byte)(data[10] & 64) != 0) pinson = pinson + "Pin 3, ";
                if ((byte)(data[10] & 128) != 0) pinson = pinson + "Pin 4, ";
                if (pinson == "") pinson = "Pins On=None";
                listBox2.Items.Add("Pins On=" + pinson);
                listBox2.Items.Add("Firmware Version=" + data[11].ToString()); //firmware version

                string temp = "PID=" + (data[13] * 256 + data[12]).ToString();
                listBox2.Items.Add(temp);

                listBox2.Items.Add("Dim Factor Bank 1=" + data[17].ToString());
                listBox2.Items.Add("Dim Factor Bank 2=" + data[18].ToString());
                txtBank1.Text = data[17].ToString();
                txtBank2.Text = data[18].ToString();

                listBox2.Items.Add("GPIO Input/Output Configuration=" + data[19].ToString());
                listBox2.Items.Add("GPIO Input Configuration=" + data[20].ToString());
                rb1O.Checked = true; rb2O.Checked = true; rb3O.Checked = true; rb4O.Checked = true;
                if ((byte)(data[19] & 1) == 1) //pin is input, check which type of input
                {
                    rb1ID.Checked = true;
                    if ((byte)(data[20] & 1) == 1) rb1I.Checked = true;
                }
                if ((byte)(data[19] & 2) == 2) //pin is input, check which type of input
                {
                    rb2ID.Checked = true;
                    if ((byte)(data[20] & 2) == 2) rb2I.Checked = true;
                }
                if ((byte)(data[19] & 4) == 4) //pin is input, check which type of input
                {
                    rb3ID.Checked = true;
                    if ((byte)(data[20] & 4) == 4) rb3I.Checked = true;
                }
                if ((byte)(data[19] & 8) == 8) //pin is input, check which type of input
                {
                    rb4ID.Checked = true;
                    if ((byte)(data[20] & 8) == 8) rb4I.Checked = true;
                }

                devices[selecteddevice].callNever = savecallbackstate;
            }
        }

        //T-bar Calibration - the T-bar is calibrated in the factory, do this only if absolutely necessary
        private void btnStartCal_Click(object sender, EventArgs e)
        {
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                wData[0] = 0;
                wData[1] = 172; //0xAC
                wData[2] = 1;
                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Start Cal";
                }
                MessageBox.Show("Move T-bar slowly up and down to full extents.  When done click Stop Cal.");
            }
        }

        private void btnStopCal_Click(object sender, EventArgs e)
        {
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                wData[0] = 0;
                wData[1] = 172; //0xAC
                wData[2] = 0;
                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Stop Cal";
                }
            }
        }

        //T-bar Invert
        private void btnTbarStandard_Click(object sender, EventArgs e)
        {
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                wData[0] = 0;
                wData[1] = 173; //0xAD
                wData[2] = 0;
                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Factory Default T-bar";
                }
            }
        }

        private void btnTbarInvert_Click(object sender, EventArgs e)
        {
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                wData[0] = 0;
                wData[1] = 173; //0xAD
                wData[2] = 1;
                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Invert T-bar";
                }
            }
        }

        //Change PIDs
        private void btnPID3_Click(object sender, EventArgs e)
        {
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 204; //0xCC
                wData[2] = (byte)cboPIDs.SelectedIndex;  

                int result=404;
				
				while(result==404){result = devices[selecteddevice].WriteData(wData);}
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;

                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Change endpoints";
                }
            }
        }

        public static String BinToHex(Byte value)
        {
            StringBuilder sb = new StringBuilder("");
            sb.Append(value.ToString("X2"));  //the 2 means 2 digits
            return sb.ToString();
        }

        public static Byte HexToBin(String value)
        {
            value = value.Trim();
            String addup = "0x" + value;
            return (Byte)Convert.ToInt32(value, 16);
        }

        //KVM Settings
        private void btnNoChange_Click(object sender, EventArgs e)
        {
            //Do not change PID on reboot
            if (selecteddevice != -1) //do nothing if not enumerated
            {

                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 196; //0xC4
                wData[2] = 0; 

                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success";
                }
            }
        }

        private void btnChange_Click(object sender, EventArgs e)
        {
            if (selecteddevice != -1) //do nothing if not enumerated
            {

                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 196; //0xC4
                wData[2] = 7;

                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success";
                }
            }
        }

        private void btnGetDataNow_Click(object sender, EventArgs e)
        {
            //After sending this command a general incoming data report will be given with
            //the 3rd byte (Data Type) 2nd bit set.  If program switch is up byte 3 will be 2
            //and if it is pressed byte 3 will be 3.  This is useful for getting the initial state
            //or unit id of the device before it sends any data.
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[0] = 0;
                wData[1] = 177; //0xB1

                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Generate Data";
                }
            }
        }

        private void btnCustom_Click(object sender, EventArgs e)
        {
            //After sending this command a custom incoming data report will be given with
            //the 3rd byte (Data Type) set to 0xE0, the 4th byte set to the count given below when the command was sent
            //and the following bytes whatever the user wishes.  In this example we are sending 3 bytes; 1, 2, 3

            if (selecteddevice != -1) //do nothing if not enumerated
            {
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[0] = 0;
                wData[1] = 224; //0xE0
                wData[2] = 3; //count of bytes to follow
                wData[3] = 1; //1st custom byte
                wData[4] = 2; //2nd custom byte
                wData[5] = 3; //3rd custom byte

                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Custom Data";
                }
            }
        }

        private void btnVersion_Click(object sender, EventArgs e)
        {
            //This report available only on v30 firmware and above
            //Write version, this is a 2 byte number that is available on enumeration.  You must reboot the device to see the 
            //newly written version!
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[0] = 0;
                wData[1] = 195; //0xC3
                wData[2] = (byte)(Convert.ToInt16(txtVersion.Text));
                wData[3] = (byte)((Convert.ToInt16(txtVersion.Text)) >> 8);
                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Write Version";
                }
                //reboot device either manually with a hotplug or using the command below, to use this uncomment out the WriteData line,
                //must re-enumerate after sending
                devices[selecteddevice].callNever = true;
                wData[0] = 0;
                wData[1] = 238; //0xEE, reboot device without unplugging
                wData[2] = 0;
                wData[3] = 0;
                result = 404;
                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Reboot";
                }
            }
        }

        private void btnTimeStamp_Click(object sender, EventArgs e)
        {
            //Sending this command will turn off the 4 bytes of data which assembled give the time in ms from the start of the computer
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 210; //0xD2
                wData[2] = 0; //0=disable, 1=enable (factory default)

                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Time Stamp";
                }
            }
        }

        private void btnTimeStampOn_Click(object sender, EventArgs e)
        {
            //Sending this command will turn on the 4 bytes of data which assembled give the time in ms from the start of the computer
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 210; //0xD2
                wData[2] = 1;  //0=disable, 1=enable (factory default)

                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Time Stamp";
                }
            }
        }

        

        private void btnSetInOut_Click(object sender, EventArgs e)
        {
            //Configure the GPIO for output or input and which type of input
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                byte gpioconfig = 0xff; //for each bit 0=output, 1=input
                byte inputconfig = 0; //if gpioconfig bit = 1 (input) then the corresponding bit in this byte gives the type of input 0=resistive pull up (short to ground), 1=resistive pull down (drive high) 
                if (rb1O.Checked == true) gpioconfig = (byte)(gpioconfig & ~1);
                else
                {
                    if (rb1I.Checked == true) { inputconfig = (byte)(inputconfig | 1); }
                }
                if (rb2O.Checked == true) gpioconfig = (byte)(gpioconfig & ~2);
                else
                {
                    if (rb2I.Checked == true) { inputconfig = (byte)(inputconfig | 2); }
                }
                if (rb3O.Checked == true) gpioconfig = (byte)(gpioconfig & ~4);
                else
                {
                    if (rb3I.Checked == true) { inputconfig = (byte)(inputconfig | 4); }
                }
                if (rb4O.Checked == true) gpioconfig = (byte)(gpioconfig & ~8);
                else
                {
                    if (rb4I.Checked == true) { inputconfig = (byte)(inputconfig | 8); }
                }

                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[0] = 0;
                wData[1] = 147; //0x93
                wData[2] = gpioconfig; //pins 1, 2, 3, 4   MSB 0-0-0-0-4-3-2-1 LSB   0=output, 1=input
                wData[3] = inputconfig; //pins 1, 2, 3, 4  MSB 0-0-0-0-4-3-2-1 LSB   0=resistive pull up, 1=resistive pull down, note: if a bit is configured as output in gpioconfig, then its bit setting here doesn't matter

                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Configure GPIO";
                }
            }
        }

        private void btnSaveInOut_Click(object sender, EventArgs e)
        {
            //Save the GPIO configuration to the eeprom
            for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
            {
                wData[j] = 0;
            }

            wData[0] = 0;
            wData[1] = 148; //0x94

            int result = 404;

            while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
            if (result != 0)
            {
                toolStripStatusLabel1.Text = "Write Fail: " + result;
            }
            else
            {
                toolStripStatusLabel1.Text = "Write Success - Save GPIO Configuration";
            }
        }

        private void btnSiliconGeneratedID_Click(object sender, EventArgs e)
        {
            //This command is only necessary if devices[].SerialNumberString is not available on enumerate
            //Sending the command will make the device return information about it
            if (selecteddevice != -1)
            {
                //IMPORTANT turn off the callback if going so data isn't grabbed there, turn it back on later
                bool savecallbackstate = devices[selecteddevice].callNever;
                devices[selecteddevice].callNever = true;

                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 157; //0x9D
                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Silicon Generated ID";
                }

                byte[] data = null;
                int countout = 0;
                data = new byte[80];

                int ret = devices[selecteddevice].BlockingReadData(ref data, 100);
                while ((ret == 0 && data[2] != 157) || ret == 304)
                {
                    if (ret == 304)
                    {
                        // Didn't get any data for 100ms, increment the countout extra
                        countout += 99;
                    }
                    countout++;
                    if (countout > 1000) //increase this if have to check more than once
                        break;
                    ret = devices[selecteddevice].BlockingReadData(ref data, 100);
                }
                string uniqueID = "";
                for (int i = 0; i < 8; i++)
                {
                    uniqueID = uniqueID + BinToHex(data[i + 3]);
                }

                lblSiliconGeneratedID.Text = uniqueID;

                devices[selecteddevice].callNever = savecallbackstate;
            }
        }

        private void btnVirtualButton_Click(object sender, EventArgs e)
        {
            //Virtually press or release a button
            //for physical buttons use the index shown below for the ID

            //Index (in decimal)
            //Columns-->
            //  0   -  8
            //  1   -  9
            //  2   -  10 
            //  3   -  11  
            //  4   6  12 
            //  5   7  13 
            //for GPIO Pin 1 ID =100, GPIO Pin 2 ID =101, GPIO Pin 3 ID=102, GPIO Pin 4 ID=103
            //for the 8 virtual buttons use ID 104-111
            //to clear ALL virtual buttons, ie all virtual buttons pressed are released, use ID=255, if using 255 then state is ignored

            if (selecteddevice != -1) //do nothing if not enumerated
            {
                int state = 1;
                if (rbRelease.Checked == true)
                {
                    state = 2;
                }
                int ID = Convert.ToInt16(txtVirtualButton.Text);

                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[0] = 0;
                wData[1] = 220; //0xDC
                wData[2] = (byte)ID; //index
                wData[3] = (byte)state; //1=press (bit set), 2=release (bit unset)

                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Virtual Button";
                }
            }
        }

        private void btnZSliderOff_Click(object sender, EventArgs e)
        {
            //Sending this command will turn off the behavior of the T-bar controlling Z Slider in Game Controller
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 144; //0x90
                wData[2] = 0; //0=disable, 1=enable (factory default)

                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Z Slider Off";
                }
            }
        }

        private void btnZSliderOn_Click(object sender, EventArgs e)
        {
            //Sending this command will turn on the behavior of the T-bar controlling Z Slider in Game Controller
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 144; //0x90
                wData[2] = 1; //0=disable, 1=enable (factory default)

                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Z Slider On";
                }
            }
        }

        private void btnSetDongle_Click(object sender, EventArgs e)
        {
            //Sets the 16 byte AES key in the X-keys, keep track of this key, it is are required for decryption
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                //pick a secret 16 byte key and save this Key!!
                myKey[0] = 7;
                myKey[1] = 58;
                myKey[2] = 33;
                myKey[3] = 243;
                myKey[4] = 7;
                myKey[5] = 58;
                myKey[6] = 33;
                myKey[7] = 243;
                myKey[8] = 7;
                myKey[9] = 58;
                myKey[10] = 33;
                myKey[11] = 243;
                myKey[12] = 7;
                myKey[13] = 58;
                myKey[14] = 33;
                myKey[15] = 243;

                //Write AES key to X-keys, this key is stored in eeprom
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 137; //0x89 Set AES Key
                for (int i = 0; i < 16; i++)
                {
                    wData[2 + i] = myKey[i];
                }

                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;

                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Set AES Dongle";
                }
            }
        }

        private void btnCheckDongle_Click(object sender, EventArgs e)
        {
            //Check dongle by encrypting a phrase and checking with C# decryption
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                //Before each encryption MUST set the initialization vector. The initialzation vector is set to all 0s after each encryption and decryption in the X-keys.   
                Random rnd = new Random();
                for (int i = 0; i < 16; i++)
                {
                    myIV[i] = (byte)rnd.Next(0, 254); //valid values are 0-255 HOWEVER all 0s is not allowed because that is interpreted as an non-initialized IV
                }

                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 138; //0x8A Set AES IV
                for (int i = 0; i < 16; i++)
                {
                    wData[2 + i] = myIV[i];
                }

                int result = 404;
                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }


                //Encrypt
                bool savecallbackstate = devices[selecteddevice].callNever;
                devices[selecteddevice].callNever = true;

                string mymessage = "Enter any phrase";
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 139; //0x8B AES Encrypt
                for (int i = 0; i < mymessage.Length; i++)
                {
                    wData[2 + i] = (byte)mymessage[i];
                }

                result = 404;
                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Check AES Dongle";
                }
                //read back the encrypted data
                byte[] encrypteddata = new byte[32];
                byte[] data = null;
                int countout = 0;
                data = new byte[80];

                int ret = devices[selecteddevice].BlockingReadData(ref data, 100);
                while ((ret == 0 && data[2] != 139) || ret == 304)
                {
                    if (ret == 304)
                    {
                        // Didn't get any data for 100ms, increment the countout extra
                        countout += 99;
                    }
                    countout++;
                    if (countout > 1000) //increase this if have to check more than once
                        break;
                    ret = devices[selecteddevice].BlockingReadData(ref data, 100);
                }
                for (int i = 0; i < 32; i++)
                {
                    encrypteddata[i] = data[i + 3];
                }

                devices[selecteddevice].callNever = savecallbackstate;

                //Decrypt
                //use the same secret 16 byte key that was used in Set Dongle and the same IV as used above to encrypt
                myKey[0] = 7;
                myKey[1] = 58;
                myKey[2] = 33;
                myKey[3] = 243;
                myKey[4] = 7;
                myKey[5] = 58;
                myKey[6] = 33;
                myKey[7] = 243;
                myKey[8] = 7;
                myKey[9] = 58;
                myKey[10] = 33;
                myKey[11] = 243;
                myKey[12] = 7;
                myKey[13] = 58;
                myKey[14] = 33;
                myKey[15] = 243;

                string decryptresults = DecryptStringFromBytes_Aes(encrypteddata, myKey, myIV, CipherMode.CBC, PaddingMode.Zeros);
                //remove padded 0s
                decryptresults = decryptresults.Replace("\0", string.Empty);
                if (mymessage == decryptresults)
                {
                    lblAESPassFail.Text = "Pass";
                    lblAESPassFail.BackColor = Color.Lime;
                }
                else
                {
                    lblAESPassFail.Text = "Fail";
                    lblAESPassFail.BackColor = Color.Red;
                }
            }
        }

        static byte[] EncryptStringToBytes_Aes(string plainText, byte[] Key, byte[] IV, CipherMode thismode, PaddingMode thispadding)
        {
            // Check arguments.
            if (plainText == null || plainText.Length <= 0)
                throw new ArgumentNullException("plainText");
            if (Key == null || Key.Length <= 0)
                throw new ArgumentNullException("Key");
            if (IV == null || IV.Length <= 0)
                throw new ArgumentNullException("IV");
            byte[] encrypted;

            // Create an Aes object
            // with the specified key and IV.
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Key;
                aesAlg.IV = IV;
                aesAlg.Mode = thismode; // CipherMode.CBC; 
                aesAlg.Padding = thispadding; // PaddingMode.Zeros;

                // Create an encryptor to perform the stream transform.
                ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

                // Create the streams used for encryption.
                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                        {
                            //Write all data to the stream.
                            swEncrypt.Write(plainText);
                        }
                        encrypted = msEncrypt.ToArray();
                    }
                }
            }

            // Return the encrypted bytes from the memory stream.

            return encrypted;
        }

        static string DecryptStringFromBytes_Aes(byte[] cipherText, byte[] Key, byte[] IV, CipherMode thismode, PaddingMode thispadding)
        {
            // Check arguments.
            if (cipherText == null || cipherText.Length <= 0)
                throw new ArgumentNullException("cipherText");
            if (Key == null || Key.Length <= 0)
                throw new ArgumentNullException("Key");
            if (IV == null || IV.Length <= 0)
                throw new ArgumentNullException("IV");

            // Declare the string used to hold
            // the decrypted text.
            string plaintext = null;

            // Create an Aes object
            // with the specified key and IV.
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Key;
                aesAlg.IV = IV; //IV not needed for ECB mode
                aesAlg.Mode = thismode; // CipherMode.CBC; 
                aesAlg.Padding = thispadding; // PaddingMode.Zeros; 

                // Create a decryptor to perform the stream transform.
                ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);


                // Create the streams used for decryption.
                using (MemoryStream msDecrypt = new MemoryStream(cipherText))
                {
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                        {

                            // Read the decrypted bytes from the decrypting stream
                            // and place them in a string.
                            plaintext = srDecrypt.ReadToEnd();
                        }
                    }
                }
            }

            return plaintext;
        }

        private void btnRawAESSetKey_Click(object sender, EventArgs e)
        {
            //Sets the 16 byte AES key in the X-keys, keep track of this key, it is are required for decryption
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                myAes.GenerateKey(); //securely generated random key
                //save this Key!!
                for (int i = 0; i < 16; i++)
                {
                    myKey[i] = myAes.Key[i];
                }
                //Write Key to X-keys, this key is stored in eeprom
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 137; //0x89 Set AES Key
                for (int i = 0; i < 16; i++)
                {
                    wData[2 + i] = myKey[i];
                }

                int result = 404;

                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;

                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Set AES key";
                }
            }
        }

        private void btnAESEncrypt_Click(object sender, EventArgs e)
        {
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                //input data (up to 32 bytes), outputs encryption
                //AES Key should have been previously set and recorded (if decrypting)

                //Before each encryption MUST set the initialization vector. The initialzation vector is set to all 0s after each encryption and decryption in the X-keys.   
                Random rnd = new Random();
                for (int i = 0; i < 16; i++)
                {
                    myIV[i] = (byte)rnd.Next(0, 254); //valid values are 0-255 HOWEVER all 0s is not allowed because that is interpreted as an non-initialized IV
                }

                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 138; //0x8A Set AES IV
                for (int i = 0; i < 16; i++)
                {
                    wData[2 + i] = myIV[i];
                }

                int result = 404;
                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }

                string mymessage = txtXkeysEncrypt.Text;
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 139; //0x8B AES Encrypt
                for (int i = 0; i < mymessage.Length; i++)
                {
                    wData[2 + i] = (byte)mymessage[i];
                }

                result = 404;
                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;

                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - AES encrypt";
                }
                //results in callback
            }
        }

        private void btnXkeysDecrypt_Click(object sender, EventArgs e)
        {
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                //input encrypted data (up to 32 bytes), outputs decryption
                //AES Key and IV should have been previously set and recorded

                //Before each decryption MUST set the initialization vector with that used for the encryption.
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 138; //0x8A Set AES IV
                for (int i = 0; i < 16; i++)
                {
                    wData[2 + i] = myIV[i];
                }
                int result = 404;
                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }

                //Decrypt
                string decryptthis = lblXkeysEncrypt.Text;
                byte[] encryptedbytes = new byte[32];
                int count = 0;
                while (decryptthis.Length > 0)
                {
                    int pos = decryptthis.IndexOf(",");
                    if (pos != -1)
                    {
                        encryptedbytes[count] = HexToBin(decryptthis.Substring(0, 2));
                        decryptthis = decryptthis.Remove(0, pos + 1).Trim();
                        count++;
                    }
                }

                //input encrypted data (up to 32 bytes), outputs decryption
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 140; //0x8C

                for (int i = 0; i < 32; i++)
                {
                    wData[2 + i] = encryptedbytes[i];
                }

                result = 404;
                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;

                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - AES decrypt";
                }
                //results in callback
            }
        }

        

      

        


        

       
        

        

        

       

        




    }
    
    
}
