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

        //for keeping track of power (optional)
        int[,] color1 = new int[3, 96]; //bank 1 [color, button] 
        int[,] color2 = new int[3, 96]; //bank 2
        int currentpowerlevel = 0;
        int maxpowerlevel = 0;
        //float reductionfactorbank1 = 1.0f; //0 to 1, = (Dim Factor for Bank 1)/255.0
        //float reductionfactorbank2 = 1.0f; //0 to 1, = (Dim Factor for Bank 2)/255.0
        int dimfactorbank1 = 255; //read in from device on start up
        int dimfactorbank2 = 255; //read in from device on start up

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

                if (data[2] < 4)
                {
                    byte val2 = (byte)(data[2] & 1);

                    //check the keyboard state byte 
                    val2 = (byte)(data[19] & 1);
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
                    val2 = (byte)(data[19] & 2);
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
                    val2 = (byte)(data[19] & 4);
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
                    val2 = (byte)(data[19] & 16);
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
                    val2 = (byte)(data[19] & 32);
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
                    val2 = (byte)(data[19] & 64);
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
                    val2 = (byte)(data[19] & 128);
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

                    //read the unit ID
                    c = this.LblUnitID;
                    this.SetText(data[1].ToString());

                    //buttons
                    //this routine is for separating out the individual button presses/releases from the data byte array.
                    int maxcols = 16; //number of columns of Xkeys digital button data, labeled "Keys" in P.I. Engineering SDK - General Incoming Data Input Report
                    int maxrows = 6; //number of rows of Xkeys digital button data
                    c = this.LblButtons;
                    string buttonsdown = "Buttons: "; //for demonstration, reset this every time a new input report received
                    this.SetText(buttonsdown);

                    for (int i = 0; i < maxcols; i++) //loop through digital button bytes 
                    {
                        for (int j = 0; j < maxrows; j++) //loop through each bit in the button byte
                        {
                            int temp1 = (int)Math.Pow(2, j); //1, 2, 4, 8, 16, 32, 64, 128
                            int keynum = maxrows * i + j; //using key numbering in sdk; column 1 = 0,1,2... column 2 = 6,7,8... column 3 = 12,13,14... column 4 = 18,19,20... etc
                            byte temp2 = (byte)(data[i + 3] & temp1); //check using bitwise AND the current value of this bit. The + 3 is because the 1st button byte starts 3 bytes in at data[3]
                            byte temp3 = (byte)(lastdata[i + 3] & temp1); //check using bitwise AND the previous value of this bit
                            int state = 0; //0=was up, now up, 1=was up, now down, 2= was down, still down, 3= was down, now up
                            if (temp2 != 0 && temp3 == 0) state = 1; //press
                            else if (temp2 != 0 && temp3 != 0) state = 2; //held down
                            else if (temp2 == 0 && temp3 != 0) state = 3; //release

                            switch (state)
                            {
                                case 1: //key was up and now is pressed
                                    buttonsdown = buttonsdown + keynum.ToString() + " ";
                                    c = this.LblButtons;
                                    SetText(buttonsdown);
                                    break;
                                case 2: //key was pressed and still is pressed
                                    buttonsdown = buttonsdown + keynum.ToString() + " ";
                                    c = this.LblButtons;
                                    SetText(buttonsdown);
                                    break;
                                case 3: //key was pressed and now released
                                    break;
                            }
                            //Perform action based on key number, consult P.I. Engineering SDK documentation for the key numbers
                            #region individualbuttons
                            switch (keynum)
                            {
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

                                //Next column of buttons
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

                                //Next column of buttons
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
                                case 14: //button 14
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 15: //button 15
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 16: //button 16
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 17: //button 17
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;

                                //Next column of buttons
                                case 18: //button 18
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 19: //button 19
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 20: //button 20
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 21: //button 21
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 22: //button 22
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 23: //button 23
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 24: //button 24
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 25: //button 25
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 26: //button 26
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 27: //button 27
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 28: //button 28
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 29: //button 29
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;

                                //Next column of buttons
                                case 30: //button 30
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 31: //button 31
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 32: //button 32
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 33: //button 33
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 34: //button 34
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 35: //button 35
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;

                                //Next column of buttons
                                case 36: //button 36
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 37: //button 37
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 38: //button 38
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 39: //button 39
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 40: //button 40
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 41: //button 41
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;

                                //Next column of buttons
                                case 42: //button 42
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 43: //button 43
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 44: //button 44
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 45: //button 45
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 46: //button 46
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 47: //button 47
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 48: //button 48
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 49: //button 49
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 50: //button 50
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 51: //button 51
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 52: //button 52
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 53: //button 53
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;

                                //Next column of buttons
                                case 54: //button 54
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 55: //button 55
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 56: //button 56
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 57: //button 57
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 58: //button 58
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 59: //button 59
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;

                                //Next column of buttons
                                case 60: //button 60
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 61: //button 61
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 62: //button 62
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 63: //button 63
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 64: //button 64
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 65: //button 65
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;

                                //Next column of buttons
                                case 66: //button 66
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 67: //button 67
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 68: //button 68
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 69: //button 69
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 70: //button 70
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 71: //button 71
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 72: //button 72
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 73: //button 73
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 74: //button 74
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 75: //button 75
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 76: //button 76
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 77: //button 77
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;

                                //Next column of buttons
                                case 78: //button 78
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 79: //button 79
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 80: //button 80
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 81: //button 81
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 82: //button 82
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 83: //button 83
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;

                                //Next column of buttons
                                case 84: //button 84
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 85: //button 85
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 86: //button 86
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 87: //button 87
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 88: //button 88
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 89: //button 89
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;

                                //Next column of buttons
                                case 90: //button 90
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 91: //button 91
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 92: //button 92
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 93: //button 93
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 94: //button 94
                                    if (state == 1) //key was pressed
                                    {
                                        //do press actions
                                    }
                                    else if (state == 3) //key was released
                                    {
                                        //do release action
                                    }
                                    break;
                                case 95: //button 95
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
                            #endregion
                        }
                    }

                    //Virtual buttons are in data[20]
                    maxcols = 1; //1 byte of virtual buttons
                    maxrows = 8; //number of rows of Xkeys digital button data
                    int virtualbuttonsbyteoffset = 20; //Virtual buttons are in data[20]
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
                    //end buttons

                    //time stamp info 4 bytes
                    long absolutetime = 16777216 * data[sourceDevice.ReadLength - 5] + 65536 * data[sourceDevice.ReadLength - 4] + 256 * data[sourceDevice.ReadLength - 3] + data[sourceDevice.ReadLength - 2];  //ms
                    long absolutetime2 = absolutetime / 1000; //seconds
                    c = this.label19;
                    this.SetText("absolute time: " + absolutetime2.ToString() + " s");
                    long deltatime = absolutetime - saveabsolutetime;
                    c = this.label20;
                    this.SetText("delta time: " + deltatime + " ms");
                    saveabsolutetime = absolutetime;
                }
                else if (data[2] == 167) //0xA7 RGB Backlight state request
                {
                    thisListBox = listBox3;
                    this.ClearListBox();
                    this.SetListBox("Button=" + (data[3]));
                    //bank 1
                    this.SetListBox("Bank 1 Red=" + (data[4]));
                    this.SetListBox("Bank 1 Green=" + (data[5]));
                    this.SetListBox("Bank 1 Blue=" + (data[6]));
                    this.SetListBox("Bank 1 Dim Factor=" + (data[14])); //255=100%
                    if (data[10] == 1) //0=no flash, 1=flashing
                        this.SetListBox("Flash Bank 1 = flashing");
                    else this.SetListBox("Flash Bank 1 = not flashing"); 
                    //bank 2
                    this.SetListBox("Bank 2 Red=" + (data[7]));
                    this.SetListBox("Bank 2 Green=" + (data[8]));
                    this.SetListBox("Bank 2 Blue=" + (data[9]));
                    this.SetListBox("Bank 2 Dim Factor=" + (data[15])); //255=100%
                    if (data[11] == 1) //0=no flash, 1=flashing
                        this.SetListBox("Flash Bank 2 = flashing");
                    else this.SetListBox("Flash Bank 2= not flashing"); 
                    this.SetListBox("Flash frequency=" + (data[13]));

                    //set these variables for tracking of power
                    color1[0, data[3]] = data[4]; //bank 1 red
                    color1[1, data[3]] = data[5]; //bank 1 green
                    color1[2, data[3]] = data[6]; //bank 1 blue
                    color2[0, data[3]] = data[7]; //bank 2 red
                    color2[1, data[3]] = data[8]; //bank 2 green
                    color2[2, data[3]] = data[9]; //bank 2 blue
                    dimfactorbank1 = data[14];
                    dimfactorbank2 = data[15];
                    //update display
                    c = txtBank1;
                    SetText(dimfactorbank1.ToString());
                    c = txtBank2;
                    SetText(dimfactorbank2.ToString());
                    c = TxtIntensity;
                    SetText(dimfactorbank1.ToString());
                    c = TxtIntensity2;
                    SetText(dimfactorbank2.ToString());
                }
                else if (data[2] == 146) //0x92 Power Levels request
                {
                    thisListBox = listBox4;
                    this.ClearListBox();
                    this.SetListBox("Current power=" + ((data[3] * 65536) + (data[4] * 256) + data[5]).ToString());
                    //note rounding effects can be significant at low RGB values
                    this.SetListBox("Maximum power=" + ((data[7] * 65536) + (data[8] * 256) + data[9]).ToString());
                    this.SetListBox("Dim factor bank 1 (255=100%)= " + data[10].ToString());
                    this.SetListBox("Dim factor bank 2 (255=100%)= " + data[11].ToString());
                    
                    //set these variables for tracking of power
                    dimfactorbank1 = data[16];
                    dimfactorbank2 = data[17];
                    currentpowerlevel = ((data[3] * 65536) + (data[4] * 256) + data[5]);
                    maxpowerlevel = ((data[7] * 65536) + (data[8] * 256) + data[9]);
                    //update display
                    c = txtBank1;
                    SetText(dimfactorbank1.ToString());
                    c = txtBank2;
                    SetText(dimfactorbank2.ToString());
                    c = TxtIntensity;
                    SetText(dimfactorbank1.ToString());
                    c = TxtIntensity2;
                    SetText(dimfactorbank2.ToString());
                    
                }
                else if ((data[2] == 165) || (data[2] == 181)) //0xA5 0r 0xB5 Power Overload from Individual Button RGB Backlight Change
                {
                    //Power overload, this message is sent when the total "power" of all illuminated RGB LEDs exceeds the maximum set for the unit
                    //The 'power' is simply the sum of the upper bank and lower bank RGB values for all buttons. For example, the factory default setting is all LEDs set to red=3, green=3, and blue=3
                    //has a power=1728. 
                    thisListBox = listBox4;
                    this.ClearListBox();
                    
                    this.SetListBox("Power Overload Detected - Dim Factors have been adjusted");
                    string thisbank = "";
                    if (data[3] == 0) thisbank = "bank 1";
                    if (data[3] == 1) thisbank = "bank 2";
                    this.SetListBox("Bank=" + thisbank); //bank user was changing when overload occurred
                    this.SetListBox("Button=" + data[4].ToString()); //button user was changing when overload occurred
                    this.SetListBox("% reduction of dim factors =" + (100 - data[5]).ToString());
                    this.SetListBox("New bank 1 dim factor =" + data[6].ToString()); //the firmware automatically adjusted dim factors when power overload is detected
                    this.SetListBox("New bank 2 dim factor =" + data[7].ToString()); //the firmware automatically adjusted dim factors when power overload is detected
                    this.SetListBox("Previous bank 1 dim factor =" + data[8].ToString());
                    this.SetListBox("Previous bank 2 dim factor =" + data[9].ToString());
                    //set these variables for tracking of power
                    dimfactorbank1 = data[6];
                    dimfactorbank2 = data[7];
                    //update display
                    c = txtBank1;
                    SetText(dimfactorbank1.ToString());
                    c = txtBank2;
                    SetText(dimfactorbank2.ToString());
                    c = TxtIntensity;
                    SetText(dimfactorbank1.ToString());
                    c = TxtIntensity2;
                    SetText(dimfactorbank2.ToString());
                }
                else if ((data[2] == 166) || (data[2] == 182)) //0xA6 0r 0xB6 Power Overload from Bank Change
                {
                    //Power overload, this message is sent when the total "power" of all illuminated RGB LEDs exceeds the maximum set for the unit
                    //The 'power' is simply the sum of the upper bank and lower bank RGB values for all buttons. For example, the factory default setting is all LEDs set to red=3, green=3, and blue=3
                    //has a power=1728. 
                    
                    thisListBox = listBox4;
                    this.ClearListBox();
                   
                    this.SetListBox("Power Overload Detected - Dim factors have been adjusted");
                    string thisbank = "";
                    if (data[3] == 0) thisbank = "bank 1";
                    if (data[3] == 1) thisbank = "bank 2";
                    this.SetListBox("Bank=" + thisbank); //bank user was changing when overload occurred
                    //data[4] unused
                    this.SetListBox("% reduction of dim factors =" + (100-data[5]).ToString());
                    this.SetListBox("New bank 1 dim factor =" + data[6].ToString());
                    this.SetListBox("New bank 2 dim factor =" + data[7].ToString());
                    this.SetListBox("Previous bank 1 dim factor =" + data[8].ToString());
                    this.SetListBox("Previous bank 2 dim factor =" + data[9].ToString());
                    //set these variables for tracking of power
                    dimfactorbank1 = data[6];
                    dimfactorbank2 = data[7];
                    //update display
                    c = txtBank1;
                    SetText(dimfactorbank1.ToString());
                    c = txtBank2;
                    SetText(dimfactorbank2.ToString());
                    c = TxtIntensity;
                    SetText(dimfactorbank1.ToString());
                    c = TxtIntensity2;
                    SetText(dimfactorbank2.ToString());
                }
                else if ((data[2] == 164) || (data[2] == 187)) //0xA4 0r 0xBB Power Overload from Dim Factor Change
                {
                    //Power overload, this message is sent when the total "power" of all illuminated RGB LEDs exceeds the maximum set for the unit
                    //The 'power' is simply the sum of the upper bank and lower bank RGB values for all buttons. For example, the factory default setting is all LEDs set to red=3, green=3, and blue=3
                    //has a power=1728 which is 96x(3+3+3)x2. 

                    thisListBox = listBox4;
                    this.ClearListBox();

                    this.SetListBox("Power Overload Detected - Dim factors have been adjusted");
                    this.SetListBox("% reduction of dim factors =" + (100 - data[5]).ToString());
                    this.SetListBox("New bank 1 dim factor =" + data[6].ToString()); //current dim factor, automatically adjusted
                    this.SetListBox("New bank 2 dim factor =" + data[7].ToString()); //current dim factor, automatically adjusted
                    this.SetListBox("Previous bank 1 dim factor =" + data[8].ToString()); //previous dim factor
                    this.SetListBox("Previous bank 2 dim factor =" + data[9].ToString()); //previous dim factor
                    this.SetListBox("Desired bank 1 dim factor =" + data[10].ToString()); //user wanted this factor but it caused overload so automatically adjusted to data[6]
                    this.SetListBox("Desired bank 2 dim factor =" + data[11].ToString()); //user wanted this factor but it caused overload so automatically adjusted to data[7]
                    //set these variables for tracking of power
                    dimfactorbank1 = data[6];
                    dimfactorbank2 = data[7];
                    //update display
                    c = txtBank1;
                    SetText(dimfactorbank1.ToString());
                    c = txtBank2;
                    SetText(dimfactorbank2.ToString());
                    c = TxtIntensity;
                    SetText(dimfactorbank1.ToString());
                    c = TxtIntensity2;
                    SetText(dimfactorbank2.ToString());
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
            CboRGBIndex.SelectedIndex = 0;
            toolStripStatusLabel1.Text = "";
            CboBL.SelectedIndex = 0;
            CboBankLegacy.SelectedIndex = 0;
            cboKeyIndexGet.SelectedIndex = 0;
            cboBank.SelectedIndex = 0;
            
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            //closeinterfaces on all devices that have been setup (SetupInterface called)
            for (int i = 0; i < CboDevices.Items.Count; i++)
            {
                devices[cbotodevice[i]].CloseInterface();
            }
            System.Environment.Exit(0);
        }

        private void CboDevices_SelectedIndexChanged(object sender, EventArgs e)
        {
            selecteddevice = cbotodevice[CboDevices.SelectedIndex];
            wData = new byte[devices[selecteddevice].WriteLength];//size write array 
            lastdata = new byte[devices[selecteddevice].ReadLength];
        }

        private void BtnCallback_Click(object sender, EventArgs e)
        {
            //setup callback if there are devices found for each device found

            if (CboDevices.SelectedIndex != -1)
            {
                for (int i = 0; i < CboDevices.Items.Count; i++)
                {
                    //use the cbotodevice array which contains the mapping of the devices in the CboDevices to the actual device IDs
                    devices[cbotodevice[i]].SetErrorCallback(this);
                    devices[cbotodevice[i]].SetDataCallback(this);
                    devices[cbotodevice[i]].callNever = false;
                }
                toolStripStatusLabel1.Text = "Callback on";
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            listBox1.Items.Clear();
        }

        private void BtnEnumerate_Click(object sender, EventArgs e)
        {
            CboDevices.Items.Clear();
            cbotodevice = new int[128]; //128=max # of devices
            //enumerate and setupinterfaces for all devices
            devices = PIEHid32Net.PIEDevice.EnumeratePIE();
            bool notme = false;
            if (devices.Length == 0)
            {
                toolStripStatusLabel1.Text = "No Devices Found";
            }
            else
            {
                //System.Media.SystemSounds.Beep.Play(); 
                int cbocount=0; //keeps track of how many valid devices were added to the CboDevice box
                for (int i = 0; i < devices.Length; i++)
                {
                    //information about device
                    //PID = devices[i].Pid);
                    //HID Usage = devices[i].HidUsage);
                    //HID Usage Page = devices[i].HidUsagePage);
                    //HID Version = devices[i].Version); //NOTE: this is NOT the firmware version which is given in the descriptor
                    int hidusagepg = devices[i].HidUsagePage;
                    int hidusage = devices[i].HidUsage;
                   
                    if (devices[i].HidUsagePage == 0xc && devices[i].WriteLength>1)
                    {
                        switch (devices[i].Pid)
                        {
                            case 1496:
                                //Device 1 Keyboard, Multimedia, Input and Output endpoints, PID #1
                                CboDevices.Items.Add(devices[i].ProductString + " (" + devices[i].Pid + "=PID #1)");
                                cbotodevice[cbocount] = i;
                                cbocount++;
                                cboPIDs.SelectedIndex = 0;
                                break;
                            case 1497:
                                //Device 2 Keyboard (boot), Multimedia, Input and Output endpoints, PID #2
                                CboDevices.Items.Add(devices[i].ProductString + " (" + devices[i].Pid + "=PID #2)");
                                cbotodevice[cbocount] = i;
                                cbocount++;
                                cboPIDs.SelectedIndex = 1;
                                break;
                            case 1498:
                                //Device 3 Keyboard, Joystick, Input and Output endpoints, PID #3
                                CboDevices.Items.Add(devices[i].ProductString + " (" + devices[i].Pid + "=PID #3)");
                                cbotodevice[cbocount] = i;
                                cbocount++;
                                cboPIDs.SelectedIndex = 2;
                                break;
                            case 1499:
                                //Device 4 Mouse, Joystick, Input and Output endpoints, PID #4
                                CboDevices.Items.Add(devices[i].ProductString + " (" + devices[i].Pid + "=PID #4)");
                                cbotodevice[cbocount] = i;
                                cbocount++;
                                cboPIDs.SelectedIndex = 3;
                                break;
                            case 1500:
                                //Device 5 Keyboard (boot), Mouse (boot), Input and Output endpoints, PID #5
                                CboDevices.Items.Add(devices[i].ProductString + " (" + devices[i].Pid + "=PID #5)");
                                cbotodevice[cbocount] = i;
                                cbocount++;
                                cboPIDs.SelectedIndex = 4;
                                break;
                            case 1501:
                                //Device 6 Input and Output endpoints, PID #6
                                CboDevices.Items.Add(devices[i].ProductString + " (" + devices[i].Pid + "=PID #6)");
                                cbotodevice[cbocount] = i;
                                cbocount++;
                                cboPIDs.SelectedIndex = 5;
                                break;
                            case 1502:
                                //Device 7 Keyboard, Joystick, Mouse, Multimedia, Input and Output endpoints, PID #7
                                CboDevices.Items.Add(devices[i].ProductString + " (" + devices[i].Pid + "=PID #7)");
                                cbotodevice[cbocount] = i;
                                cbocount++;
                                cboPIDs.SelectedIndex = 6;
                                break;
                            default:
                                CboDevices.Items.Add("Unknown Device: " + devices[i].ProductString + " (" + devices[i].Pid + ")");
                                cbotodevice[cbocount] = i;
                                cbocount++;
                                cboPIDs.SelectedIndex = 0;
                                notme = true;
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
                            CboDevices.Items.Add("Bootload device: " + devices[i].ProductString + " (" + devices[i].Pid + ")");
                            cbotodevice[cbocount] = i;
                            cbocount++;
                            DisableAllControls(devices[i].Pid);
                        }
                        else if (devices[i].Pid == 1503)
                        {
                            //Device 11 Keyboard only endpoint
                            CboDevices.Items.Add(devices[i].ProductString + " KVM (" + devices[i].Pid + "=PID #8), ID: " + i);
                            cbotodevice[cbocount] = i;
                            cbocount++;
                            cboPIDs.SelectedIndex = 7;
                            DisableAllControls(devices[i].Pid);
                        }
                    }
                }
            }
            if ((CboDevices.Items.Count > 0))
            {
                CboDevices.SelectedIndex = 0;
                selecteddevice = cbotodevice[CboDevices.SelectedIndex];
                wData = new byte[devices[selecteddevice].WriteLength];//go ahead and setup for write
                lastdata = new byte[devices[selecteddevice].ReadLength];
                LblVersion.Text = devices[selecteddevice].Version.ToString();
                lblSiliconGeneratedID.Text = devices[selecteddevice].SerialNumberString;
                toolStripStatusLabel1.Text = devices[selecteddevice].ProductString + " found";
                if (notme == false)
                {
                    GetRGBLEDValues(); //do this once then keep track in application, time consuming!
                    GetPowerLevels();
                    ComputePowerLevel(); //after filling in the color1 and color2 arrays, compute the power level. This is just a demo of how to keep track of power level in application
                }
            }
           
        }

        private void DisableAllControls(int thispid)
        {
            foreach (Control cl in Controls)
            {
                if (cl.Name != "BtnEnumerate")
                {
                    cl.Enabled = false;

                }
            }
           
            if (thispid == 1503)
                MessageBox.Show("Device in PID #8 (KVM), no input or output reports are available. To exit KVM mode, replug device into usb port and immediately after press Scroll Lock 10-15 times.");
            else if (thispid == 1292) //if see this pid, contact tech support: tech@piengineering.com
                MessageBox.Show("Device in bootloader mode. Contact P.I. Engineering.");
            
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

        private void BtnRed_Click(object sender, EventArgs e)
        {
            //Turn on the red LED
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                byte saveled = wData[2]; //save the current value of the LED byte
                for (int j = 0; j <devices[selecteddevice].WriteLength; j++) 
                {
                    wData[j] = 0;
                }
                wData[1] = 186;
                wData[2] = (byte)(saveled | 128);

                int result=404;
				
				while(result==404){result = devices[selecteddevice].WriteData(wData);}
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - Red LED";
                }
            }

        }

        private void BtnUnitID_Click(object sender, EventArgs e)
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

        private void BtnLegacyIntensities_Click(object sender, EventArgs e)
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
                wData[2] = (byte)(Convert.ToInt16(TxtIntensity.Text)); ; //0-255 for brightness of bank 1 bl leds
                wData[3] = (byte)(Convert.ToInt16(TxtIntensity2.Text)); ; //0-255 for brightness of bank 2 bl leds

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

        private void BtnBLToggle_Click(object sender, EventArgs e)
        {
            //Sending this command toggles the backlights
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[0] = 0;
                wData[1] = 184;

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

        private void ChkBank1OnOff_CheckedChanged(object sender, EventArgs e)
        {
            //Legacy Backlight (blue/red style), sets all top bank LEDs to blue
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                byte sl = 0;

                if (ChkBank1OnOff.Checked == true) sl = 255; //all rows
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[0] = 0;
                wData[1] = 182; //0xb6
                wData[2] = 0;  //0 for Blue, 1 for Red
                wData[3] = (byte)sl; //OR turn individual rows on or off using bits.  1st bit=row 1, 2nd bit=row 2, 3rd bit =row 3, etc

              
                int result=404;
				
				while(result==404){result = devices[selecteddevice].WriteData(wData);}
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - All Blue BL on(blue)/off";
                }
            }
        }

        private void ChkBank2OnOff_CheckedChanged(object sender, EventArgs e)
        {
            //Legacy Backlight (blue/red style), sets all bottom bank LEDs to red
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                byte sl = 0;
              
                if (ChkBank2OnOff.Checked == true) sl = 255; //all rows
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[0] = 0;
                wData[1] = 182; //0xb6
                wData[2] = 1;  //0 for Blue, 1 for Red
                wData[3] = (byte)sl; //OR turn individual rows on or off using bits.  1st bit=row 1, 2nd bit=row 2, 3rd bit =row 3, etc
               
                int result=404;
				
				while(result==404){result = devices[selecteddevice].WriteData(wData);}
                if (result != 0)
                {
                    toolStripStatusLabel1.Text = "Write Fail: " + result;
                }
                else
                {
                    toolStripStatusLabel1.Text = "Write Success - All Red BL on(red)/off";
                   
                }
            }
        }

        private void BtnSetFlash_Click(object sender, EventArgs e)
        {
            //Sets the frequency of flashing for both the LEDs and backlighting
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[0] = 0;
                wData[1] = 180; // 0xb4
                wData[2] = (byte)(Convert.ToInt16(TxtFlashFreq.Text));

                int result=404;
				
				while(result==404){result = devices[selecteddevice].WriteData(wData);}
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

        private void BtnSaveBL_Click(object sender, EventArgs e)
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
                int result=404;
				
				while(result==404){result = devices[selecteddevice].WriteData(wData);}
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

        private void BtnTimeStamp_Click(object sender, EventArgs e)
        {
            //Sending this command will turn off the 4 bytes of data which assembled give the time in ms from the start of the computer
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 210;
                wData[2] = 0;

                int result=404;
				
				while(result==404){result = devices[selecteddevice].WriteData(wData);}
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

        private void BtnTimeStampOn_Click(object sender, EventArgs e)
        {
            //Sending this command will turn on the 4 bytes of data which assembled give the time in ms from the start of the computer
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 210;
                wData[2] = 1;  //default ON

                int result=404;
				
				while(result==404){result = devices[selecteddevice].WriteData(wData);}
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

        private void BtnKBreflect_Click(object sender, EventArgs e)
        {
            //Sends native keyboard messages
            //Write some keys to the textbox, should be Abcd
            //send some hid codes to the textbox, these will be coming in on the native keyboard endpoint
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                int result;
                textBox1.Focus();
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 201;

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

        private void BtnJoyreflect_Click(object sender, EventArgs e)
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
                wData[1] = 202;    //0xca
                wData[2] = (byte)Math.Abs((Convert.ToByte(textBox2.Text) ^ 127) - 255);  //X, in raw form 0 to 127 from center to right, 255 to 128 from center to left but I like to use 0-255 where 0 is max left, 255 is max right
                wData[3] = (byte)(Convert.ToByte(textBox3.Text) ^ 127); //Y, raw data 0 to 127 from center down, 255 to 128 from center up, I convert so I can enter 0-255
                wData[4] = (byte)(Convert.ToByte(textBox12.Text) ^ 127); //Z rotation, raw data 0 to 127 from center down, 255 to 128 from center up, I convert so I can enter 0-255
                wData[5] = (byte)(Convert.ToByte(textBox4.Text) ^ 127); //Z, raw data 0 to 127 from center down, 255 to 128 from center up, I convert so I can enter 0-255
                wData[6] = (byte)(Convert.ToByte(textBox13.Text) ^ 127); //Slider rotation, raw data 0 to 127 from center down, 255 to 128 from center up, I convert so I can enter 0-255

                wData[7] = Convert.ToByte(textBox5.Text); //buttons 1-8, where bit 1 is button 1, bit 1 is button 2, etc.
                wData[8] = Convert.ToByte(textBox7.Text); //buttons 9-16
                wData[9] = Convert.ToByte(textBox8.Text); //buttons 17-24
                wData[10] = Convert.ToByte(textBox9.Text); //buttons 25-32

                wData[11] = 0;

                wData[12] = Convert.ToByte(textBox6.Text); //hat, where 0 is straight up, 1 is 45deg cw, etc and 8 is no hat
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

        private void BtnDescriptor_Click(object sender, EventArgs e)
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
                wData[1] = 214;
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
                listBox2.Items.Add("Keymapstart=0x" + BinToHex(data[14]) + BinToHex(data[4]));
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
                
                listBox2.Items.Add("Dim factor Bank 1=" + data[17].ToString());
                listBox2.Items.Add("Dim factor Bank 2=" + data[18].ToString());
                dimfactorbank1 = data[17];
                dimfactorbank2 = data[18];
                //update display
                txtBank1.Text = dimfactorbank1.ToString();
                txtBank2.Text = dimfactorbank2.ToString();
                TxtIntensity.Text = dimfactorbank1.ToString();
                TxtIntensity2.Text = dimfactorbank2.ToString();

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

       

        private void BtnGetDataNow_Click(object sender, EventArgs e)
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
                wData[1] = 177; //0xb1

                int result=404;
				
				while(result==404){result = devices[selecteddevice].WriteData(wData);}
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

        private void BtnMousereflect_Click(object sender, EventArgs e)
        {
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                wData[0] = 0;
                wData[1] = 203;    //0xcb
                wData[2] = Convert.ToByte(textBox14.Text); //Buttons; 1=Left, 2=Right, 4=Center, 8=XButton1, 16=XButton2
                wData[3] = Convert.ToByte(textBox16.Text); //Mouse X motion. 128=0 no motion, 1-127 is right, 255-129=left, finest inc (1 and 255) to coarsest (127 and 129).
                wData[4] = Convert.ToByte(textBox15.Text); //Mouse Y motion. 128=0 no motion, 1-127 is down, 255-129=up, finest inc (1 and 255) to coarsest (127 and 129).
                wData[5] = Convert.ToByte(textBox11.Text);//Wheel Y. 128=0 no motion, 1-127 is up, 255-129=down, finest inc (1 and 255) to coarsest (127 and 129).
                devices[selecteddevice].WriteData(wData);

                //now send all 0s
                wData[0] = 0;
                wData[1] = 203;    //0xcb
                wData[2] = 0; //buttons
                wData[3] = 0; //X
                wData[4] = 0; //Y
                wData[5] = 0; //wheel Y
                devices[selecteddevice].WriteData(wData);
            }
        }

        private void BtnPID3_Click(object sender, EventArgs e)
        {
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 204; //0xcc
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

        private void ChkSuppress_CheckedChanged(object sender, EventArgs e)
        {
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                if (ChkSuppress.Checked == false)
                {
                    devices[selecteddevice].suppressDuplicateReports = false;
                }
                else
                {
                    devices[selecteddevice].suppressDuplicateReports = true;
                }
            }
        }
       
        private void BtnCustom_Click(object sender, EventArgs e)
        {
            //This report available only on v30 firmware and above
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
                wData[1] = 224; //0xe0
                wData[2] = 3; //count of bytes to follow
                wData[3] = 1; //1st custom byte
                wData[4] = 2; //2nd custom byte
                wData[5] = 3; //3rd custom byte

                
                int result=404;
				
				while(result==404){result = devices[selecteddevice].WriteData(wData);}
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

        private void BtnMultiMedia_Click(object sender, EventArgs e)
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
                wData[1] = 0xe1;
                wData[2] = HexToBin(TxtMMLow.Text); //Usage ID lo byte see hut1_12.pdf, pages 75-85 Consumer Page
                wData[3] = HexToBin(TxtMMHigh.Text); //Usage ID hi byte see hut1_12.pdf, pages 75-85 Consumer Page

                result=404;
				while(result==404){result = devices[selecteddevice].WriteData(wData);}
                

                wData[0] = 0;
                wData[1] = 0xe1;
                wData[2] = 0; //terminate
                wData[3] = 0; //terminate
                result=404;
				while(result==404){result = devices[selecteddevice].WriteData(wData);}
                

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

        public static Byte HexToBin(String value)
        {
            value = value.Trim();
            String addup = "0x" + value;
            return (Byte)Convert.ToInt32(value, 16);
        }

        private void BtnMyComputer_Click(object sender, EventArgs e)
        {
            //Multimedia available on v30 firmware or above.
            if (selecteddevice != -1) //do nothing if not enumerated
            {

                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[0] = 0;
                wData[1] = 0xe1;
                wData[2] = HexToBin("94"); //Usage ID lo byte see hut1_12.pdf, pages 75-85 Consumer Page
                wData[3] = HexToBin("01"); //Usage ID hi byte see hut1_12.pdf, pages 75-85 Consumer Page

                int result=404;
				
				while(result==404){result = devices[selecteddevice].WriteData(wData);}

                wData[0] = 0;
                wData[1] = 0xe1;
                wData[2] = 0; //terminate
                wData[3] = 0; //terminate
                result=404;
				while(result==404){result = devices[selecteddevice].WriteData(wData);}

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

        private void BtnSleep_Click(object sender, EventArgs e)
        {
            //Multimedia available on v30 firmware or above.
            if (selecteddevice != -1) //do nothing if not enumerated
            {
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[0] = 0;
                wData[1] = 0xe2;
                wData[2] = 2; //1=power down, 2=sleep, 4=wake up

                int result=404;
				
				while(result==404){result = devices[selecteddevice].WriteData(wData);}
                //NOTE this needs to be on the release of the key!!

                System.Threading.Thread.Sleep(1000); //this to simulate press/release

                wData[0] = 0;
                wData[1] = 0xe2;
                wData[2] = 0;

                result=404;
				while(result==404){result = devices[selecteddevice].WriteData(wData);}

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

        private void BtnVersion_Click(object sender, EventArgs e)
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
                wData[1] = 195; //c3
                wData[2] = (byte)(Convert.ToInt16(TxtVersion.Text));
                wData[3] = (byte)((Convert.ToInt16(TxtVersion.Text)) >> 8);
                int result=404;
				
				while(result==404){result = devices[selecteddevice].WriteData(wData);}
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
                wData[1] = 238; //0xee, reboot device without unplugging
                wData[2] = 0;
                wData[3] = 0;
                result=404;
				while(result==404){result = devices[selecteddevice].WriteData(wData);}
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

        


        private void BtnNoChange_Click(object sender, EventArgs e)
        {
            //Do not change PID on reboot
            if (selecteddevice != -1) //do nothing if not enumerated
            {

                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 196; //0xc4
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

        private void BtnChange_Click(object sender, EventArgs e)
        {
            if (selecteddevice != -1) //do nothing if not enumerated
            {

                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 196; //0xc4
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

        private void btnGetBLState_Click(object sender, EventArgs e)
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
                wData[1] = 179; //0xb3
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

        private void ChkBLOnOff_CheckStateChanged(object sender, EventArgs e)
        {
            //Legacy Backlight (blue/red style)
            //Use the Set Flash Freq to control frequency of blink
            //Button Index (in decimal)
            //Columns-->
            //Bank 1 (Upper) LEDs
            //  0   6   12  18 ... 90
            //  1   7   13  19 ... 91
            //  2   8   14  20 ... 92
            //  3   9   15  21 ... 93
            //  4   10  16  22 ... 94
            //  5   11  17  23 ... 95
            //Bank 2 (Lower) LEDs
            //  96   102  108  114 ... 186
            //  97   103  109  115 ... 187
            //  98   104  110  116 ... 188
            //  99   105  111  117 ... 189
            //  100  106  112  118 ... 190
            //  101  107  113  119 ... 191
            if (selecteddevice != -1)
            {
                //first get selected index
                string sindex = CboBL.Text;
                int bankindex = CboBankLegacy.SelectedIndex;
                int iindex = CboBL.SelectedIndex;
                int legacyindex=iindex;
                if (bankindex == 1) legacyindex = iindex + 96;
               
                //now get state
                int state = (int)ChkBLOnOff.CheckState;

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
            //Use the Set Flash Freq to control frequency of blink
            //Button Index (in decimal)
            //Columns-->
            //  0   6   12  18  24  30  36  42  48  54  60  66  72  78  84  90
            //  1   7   13  19  25  31  37  43  49  55  61  67  73  79  85  91
            //  2   8   14  20  26  32  38  44  50  56  62  68  74  80  86  92
            //  3   9   15  21  27  33  39  45  51  57  63  69  75  81  87  93
            //  4   10  16  22  28  34  40  46  52  58  64  70  76  82  88  94
            //  5   11  17  23  29  35  41  47  53  59  65  71  77  83  89  95

            //Upper LEDs are bank 1, bankindex = 0
            //Lower LEDs are bank 2, bankindex = 1

            if (CboDevices.SelectedIndex != -1)
            {
                byte buttonindex = Convert.ToByte(CboRGBIndex.Text);
                byte bankindex = (byte)cboBank.SelectedIndex;
                int checkstate = (int)chkRGBFlash.CheckState;
               
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                int result = 0;
                if ((bankindex == 0) || (bankindex == 1))
                {
                    wData[1] = 165; //0xA5
                    wData[2] = buttonindex; //0-95
                    wData[3] = bankindex; //0=bank 1 (top), 1=bank 2 (bottom)
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
                        toolStripStatusLabel1.Text = "Write Success - RGB Backlight";
                    }
                }
                else if (bankindex == 2) //do both
                {
                    //bank 1
                    wData[1] = 165; //0xA5
                    wData[2] = buttonindex;
                    wData[3] = 0; //0=bank 1 (top), 1=bank 2 (bottom)
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
                        toolStripStatusLabel1.Text = "Write Success - RGB Backlight";
                       
                    }

                    //bank 2
                    wData[1] = 165; //0xA5
                    wData[2] = buttonindex;
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
                        toolStripStatusLabel1.Text = "Write Success - RGB Backlight";
                    }
                   
                }
               
            }
            
        }

        private void btnSetAllBank1_Click(object sender, EventArgs e)
        {
            if (CboDevices.SelectedIndex != -1)
            {
                
                int result = 0;

                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[1] = 166;//0xA6
                wData[2] = 0; //0=Bank 1 (top LEDs), 1=Bank 2 (bottom LEDs)
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
            if (CboDevices.SelectedIndex != -1)
            {
               
                int result = 0;

                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }

                wData[1] = 166; //0xA6
                wData[2] = 1; //0=Bank 1 (top LEDs), 1=Bank 2 (bottom LEDs)
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
            if (CboDevices.SelectedIndex != -1) //do nothing if not enumerated
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

        private void Form1_Shown(object sender, EventArgs e)
        {
           cboColor.SelectedIndex = 1;
        }

        private void btnPower_Click(object sender, EventArgs e)
        {
            
        }

        private void btnGetPower_Click(object sender, EventArgs e)
        {
            //Get RGB Backlight Power Levels
            GetPowerLevels();
            
        }

        private void GetRGBLEDValues()
        {
            //Get all the LED values using Request RGB Backlight Values (167), 
            //this is time consuming so do only once and keep track programmatically thereafter
            if (CboDevices.SelectedIndex != -1)
            {
                bool savecallbackstate = devices[selecteddevice].callNever;
                devices[selecteddevice].callNever = true;
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 167; //0xA7
                
                for (int i = 0; i < 96; i++) //for each button
                {
                    wData[2] = (byte)i;
                    int result = 404;
                    while (result == 404) { result = devices[selecteddevice].WriteData(wData); }

                    byte[] data = null;
                    int countout = 0;
                    data = new byte[80];

                    int ret = devices[selecteddevice].BlockingReadData(ref data, 100);
                    while ((ret == 0 && data[2] != 167) || ret == 304)
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
                    //returned values
                    //data[3]: button for which RGB LED information is requested
                    //data[4]: bank 1 red
                    //data[5]: bank 1 green
                    //data[6]: bank 1 blue
                    //data[7]: bank 2 red
                    //data[8]: bank 2 green
                    //data[9]: bank 2 blue
                    //data[10]: flash state for bank 1
                    //data[11]: flash state for bank 2
                    //data[12]: n/a
                    //data[13]: flash frequency
                    //data[14]: dim factor bank 1
                    //data[15]: dim factor bank 2

                    //these dim factors are the same for all LEDs on a bank
                    dimfactorbank1 = data[14];
                    dimfactorbank2 = data[15];
                    color1[0, i] = data[4];
                    color1[1, i] = data[5];
                    color1[2, i] = data[6];

                    color2[0, i] = data[7];
                    color2[1, i] = data[8];
                    color2[2, i] = data[9];
                    
                }
                txtBank1.Text = dimfactorbank1.ToString();
                txtBank2.Text = dimfactorbank2.ToString();
                TxtIntensity.Text = dimfactorbank1.ToString();
                TxtIntensity2.Text = dimfactorbank2.ToString();
                
                devices[selecteddevice].callNever = savecallbackstate;
            }
        }

        private void GetPowerLevels()
        {
            //Request the power levels from device using Get RGB Backligth Power Levels output report (146).
            //Note the current power is the sum of all of the rgb LED values, maximum power is the highest power allowed, Bank 1 power is the sum of all the bank 1 rgb LED values
            //Bank 2 power is the sum of all the bank 2 rgb LED values. Bank 1 power + Bank 2 power = current power
            if (selecteddevice != -1)
            {
                bool savecallbackstate = devices[selecteddevice].callNever;
                devices[selecteddevice].callNever = true;
                for (int j = 0; j < devices[selecteddevice].WriteLength; j++)
                {
                    wData[j] = 0;
                }
                wData[0] = 0;
                wData[1] = 146; //0x92
                currentpowerlevel = 0;
                
                int result = 404;
                while (result == 404) { result = devices[selecteddevice].WriteData(wData); }
                

                byte[] data = null;
                int countout = 0;
                data = new byte[80];

                int ret = devices[selecteddevice].BlockingReadData(ref data, 100);
                while ((ret == 0 && data[2] != 146) || ret == 304)
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

                currentpowerlevel = (data[3] * 65536) + (data[4] * 256) + data[5]; //same as the power level calculated power using Request RGB Backlight Values (167)
                maxpowerlevel=(data[7] * 65536) + (data[8] * 256) + data[9];
                dimfactorbank1 = data[10];
                dimfactorbank2 = data[11];
                //update the displayed values

                listBox4.Items.Clear();
                listBox4.Items.Add("Current power=" + (currentpowerlevel).ToString());
                //note rounding effects can be significant at low RGB values
                listBox4.Items.Add("Maximum power=" + (maxpowerlevel).ToString());
                listBox4.Items.Add("Dim factor bank 1 (255=100%): " + dimfactorbank1.ToString());
                listBox4.Items.Add("Dim factor bank 2 (255=100%): " + dimfactorbank2.ToString());

                txtBank1.Text =dimfactorbank1.ToString();
                txtBank2.Text=dimfactorbank2.ToString();
                TxtIntensity.Text=dimfactorbank1.ToString();
                TxtIntensity2.Text=dimfactorbank2.ToString();
                
                
                devices[selecteddevice].callNever = savecallbackstate;
            }
        }
      
        private bool CheckNewPowerLevels(int opcode, int bank, int button, int red, int green, int blue, int thisdimfactorbank1, int thisdimfactorbank2)
        {
            //For tracking power when an LED is changed (optional)
            //Check if new led setting will overload, if overload then prompt user
            
            //Calculate the current power level, this is faster than reading it via the Get RGB Backlight Power Level output report 
            //but requires keeping track of color1 and color2 and dim factors programmatically
            int rgbtotalsbank1 = 0;
            int rgbtotalsbank2 = 0;
            for (int i = 0; i < 96; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    rgbtotalsbank1 += color1[j, i];
                }
            }
            
            for (int i = 0; i < 96; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    rgbtotalsbank2 += color2[j, i];
                }
            }
            #region individual_button
            if ((opcode == 165) || (opcode == 181)) //user changing just a single led
            {
                int newRGBvaluesforthisLED = red + green + blue;
                int oldRGBvaluesforthisLED = 0;
                if (bank == 0) //bank 1
                {
                    oldRGBvaluesforthisLED = color1[0, button] + color1[1, button] + color1[2, button];
                    rgbtotalsbank1 = rgbtotalsbank1 - oldRGBvaluesforthisLED + newRGBvaluesforthisLED;
                }
                else //bank 2
                {
                    oldRGBvaluesforthisLED = color2[0, button] + color2[1, button] + color2[2, button];
                    rgbtotalsbank2 = rgbtotalsbank2 - oldRGBvaluesforthisLED + newRGBvaluesforthisLED;
                }

                int newpower = (int)(((Convert.ToDouble(rgbtotalsbank1) * (thisdimfactorbank1 / 255.0f)) + (Convert.ToDouble(rgbtotalsbank2) * (thisdimfactorbank2 / 255.0f))) + 0.5); //+0.5 rounds to nearest int
                if (newpower > maxpowerlevel)
                {
                    return true;
                }
            }
            #endregion
            #region entire_bank
            else if (opcode == 166) //changing whole bank
            {
                if (bank == 0) //bank 1
                {
                    rgbtotalsbank1 = (red + blue + green) * 96;
                }
                else //bank 2
                {
                    rgbtotalsbank2 = (red + blue + green) * 96;
                }
                int newpower = (int)(((Convert.ToDouble(rgbtotalsbank1) * (thisdimfactorbank1 / 255.0f)) + (Convert.ToDouble(rgbtotalsbank2) * (thisdimfactorbank2 / 255.0f))) + 0.5); //+ 0.5 rounds to nearest int
                if (newpower > maxpowerlevel)
                {
                    return true;
                }
            } 
            #endregion
            #region legacy_rows
            else if (opcode == 182) //changing rows, blue/red legacy style
            {
                int rows = button;  //button is the rows in this case
                if (rows == 0) //user is turning off all RGB backlights, not issue with overpowering
                {

                }
                else 
                {
                    int count = Convert.ToString(rows, 2).ToCharArray().Count(c => c == '1'); //find out how many rows user is turning on
                    int newRGBvaluesfortheserows = count * 16 * (red + green + blue);
                    int oldRGBvaluesfortheserows = 0;
                    if (bank == 0) //bank 1
                    {
                        byte val2 = (byte)(rows & 1);
                        if (val2 == 1)
                        {
                            for (int i = 0; i < 16; i++)
                            {
                                oldRGBvaluesfortheserows += (color1[0, i * 6] + color1[1, i * 6] + color1[2, i * 6]);
                            }
                        }
                        val2 = (byte)(rows & 2);
                        if (val2 == 1)
                        {
                            for (int i = 0; i < 16; i++)
                            {
                                oldRGBvaluesfortheserows += (color1[0, (i * 6)+1] + color1[1, (i * 6)+1] + color1[2, (i * 6)+1]);
                            }
                        }
                        val2 = (byte)(rows & 4);
                        if (val2 == 1)
                        {
                            for (int i = 0; i < 16; i++)
                            {
                                oldRGBvaluesfortheserows += (color1[0, (i * 6)+2] + color1[1, (i * 6)+2] + color1[2, (i * 6)+2]);
                            }
                        }
                        val2 = (byte)(rows & 8);
                        if (val2 == 1)
                        {
                            for (int i = 0; i < 16; i++)
                            {
                                oldRGBvaluesfortheserows += (color1[0, (i * 6)+3] + color1[1, (i * 6)+3] + color1[2, (i * 6)+3]);
                            }
                        }
                        val2 = (byte)(rows & 16);
                        if (val2 == 1)
                        {
                            for (int i = 0; i < 16; i++)
                            {
                                oldRGBvaluesfortheserows += (color1[0, (i * 6)+4] + color1[1, (i * 6)+4] + color1[2, (i * 6)+4]);
                            }
                        }
                        val2 = (byte)(rows & 32);
                        if (val2 == 1)
                        {
                            for (int i = 0; i < 16; i++)
                            {
                                oldRGBvaluesfortheserows += (color1[0, (i * 6)+5] + color1[1, (i * 6)+5] + color1[2, (i * 6)+5]);
                            }
                        }
                        rgbtotalsbank1 = rgbtotalsbank1 - oldRGBvaluesfortheserows + newRGBvaluesfortheserows;
                    }
                    else //bank 2
                    {
                        byte val2 = (byte)(rows & 1);
                        if (val2 == 1)
                        {
                            for (int i = 0; i < 16; i++)
                            {
                                oldRGBvaluesfortheserows += (color2[0, i * 6] + color2[1, i * 6] + color2[2, i * 6]);
                            }
                        }
                        val2 = (byte)(rows & 2);
                        if (val2 == 1)
                        {
                            for (int i = 0; i < 16; i++)
                            {
                                oldRGBvaluesfortheserows += (color2[0, (i * 6) + 1] + color2[1, (i * 6) + 1] + color2[2, (i * 6) + 1]);
                            }
                        }
                        val2 = (byte)(rows & 4);
                        if (val2 == 1)
                        {
                            for (int i = 0; i < 16; i++)
                            {
                                oldRGBvaluesfortheserows += (color2[0, (i * 6) + 2] + color2[1, (i * 6) + 2] + color2[2, (i * 6) + 2]);
                            }
                        }
                        val2 = (byte)(rows & 8);
                        if (val2 == 1)
                        {
                            for (int i = 0; i < 16; i++)
                            {
                                oldRGBvaluesfortheserows += (color2[0, (i * 6) + 3] + color2[1, (i * 6) + 3] + color2[2, (i * 6) + 3]);
                            }
                        }
                        val2 = (byte)(rows & 16);
                        if (val2 == 1)
                        {
                            for (int i = 0; i < 16; i++)
                            {
                                oldRGBvaluesfortheserows += (color2[0, (i * 6) + 4] + color2[1, (i * 6) + 4] + color2[2, (i * 6) + 4]);
                            }
                        }
                        val2 = (byte)(rows & 32);
                        if (val2 == 1)
                        {
                            for (int i = 0; i < 16; i++)
                            {
                                oldRGBvaluesfortheserows += (color2[0, (i * 6) + 5] + color2[1, (i * 6) + 5] + color2[2, (i * 6) + 5]);
                            }
                        }
                        rgbtotalsbank2 = rgbtotalsbank2 - oldRGBvaluesfortheserows + newRGBvaluesfortheserows;
                    }

                    int newpower = (int)(((Convert.ToDouble(rgbtotalsbank1) * (thisdimfactorbank1 / 255.0f)) + (Convert.ToDouble(rgbtotalsbank2) * (thisdimfactorbank2 / 255.0f))) + 0.5); //+0.5 rounds to nearest int
                    if (newpower > maxpowerlevel)
                    {
                        return true;
                    }
                }
            }
            #endregion
            #region dim_factors
            else if (opcode == 164 || opcode == 187) //setting dim factors
            {
                //thisdimfactorbank1 and thisdimfactorbank2 are the new desired factors
                int newpower = (int)(((Convert.ToDouble(rgbtotalsbank1) * (thisdimfactorbank1 / 255.0f)) + (Convert.ToDouble(rgbtotalsbank2) * (thisdimfactorbank2 / 255.0f))) + 0.5); //+0.5 rounds to nearest int
                if (newpower > maxpowerlevel)
                {
                    return true;
                }
            }
            #endregion


            return false; //no power overload
        }

        private void ComputePowerLevel()
        {
            //Using the color1, color2 and dim factors compute the power level, this is for demonstration purposes only, the XBK-16x6 will automatically
            //deal with power overloads
            int thispower = 0;
           
            for (int i = 0; i < 96; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    float temp = color1[j, i] * (dimfactorbank1 / 255.0f);
                    int thisval = (int)(temp + 0.5);
                    thispower += thisval;
                }
            }

            for (int i = 0; i < 96; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    float temp = color2[j, i] * (dimfactorbank2 / 255.0f);
                    int thisval = (int)(temp + 0.5);
                    thispower += thisval;
                }
            }
            
           
        }

        private void btnSetInOut_Click(object sender, EventArgs e)
        {
            //Configure the GPIO for output or input and which type of input
            if (CboDevices.SelectedIndex != -1) //do nothing if not enumerated
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
            //  0   6   12  18  24  30  36  42  48  54  60  66  72  78  84  90
            //  1   7   13  19  25  31  37  43  49  55  61  67  73  79  85  91
            //  2   8   14  20  26  32  38  44  50  56  62  68  74  80  86  92
            //  3   9   15  21  27  33  39  45  51  57  63  69  75  81  87  93
            //  4   10  16  22  28  34  40  46  52  58  64  70  76  82  88  94
            //  5   11  17  23  29  35  41  47  53  59  65  71  77  83  89  95
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

        private void BtnSetDongle_Click(object sender, EventArgs e)
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

        private void BtnCheckDongle_Click(object sender, EventArgs e)
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
       
        

        

        

       

        




    }
    
    
}
