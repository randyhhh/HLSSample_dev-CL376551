using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace HLSPlayerApplication
{
    public class CC608Parser
    {
        /// <summary>
        /// Event handler for updating the closed caption text rendered on the screen. This 
        /// event is triggered whenever the rendered closed caption text on the screen needs 
        /// to be updated. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public delegate void CCUpdateEventHandler(object sender, CCUpdateEventArgs e);

        /// <summary>
        /// Event argument used by CCUpdateEventHandler. The parsed closed caption data are 
        /// passed back as a collection of text Runs which can be just added to a TextBlock 
        /// inlines. These text Runs may include color information or text decorations and 
        /// style such as italic or underlined. 
        /// </summary>
        public class CCUpdateEventArgs
        {
            public CCUpdateEventArgs(List<Run> textRunList) 
            { 
                TextRunList = textRunList; 
            }
            
            public List<Run> TextRunList { get; private set; }
        }

        /// <summary>
        /// Event handler for updating the closed caption text rendered on the screen.
        /// </summary>
        public event CCUpdateEventHandler CCUpdateEvent;
        
        /// <summary>
        /// A collection of Runs that hold the formatted CC text that is to be rendered. 
        /// </summary>
        private List<Run> _textRunList= new List<Run>();

        /// <summary>
        /// The current text Run. The incoming CC text data are parsed and appended 
        /// to this Run. 
        /// </summary>
        private Run _currentTextRun;
        
        /// <summary>
        /// Helper method to create a new text Run that is formatted with default font 
        /// size and style for 608 CC data.
        /// </summary>
        /// <returns></returns>
        private Run DefaultCC608Run()
        {
            Run run = new Run();

            run.FontFamily = new FontFamily("Segoe Xbox Regular");
            run.FontSize = 16.00;
            run.Foreground = new SolidColorBrush(Colors.Yellow);
            run.Text = "";

            _currentRow = 0;

            return run;
        }
        
        /// <summary>
        /// Method for raising the update event.  
        /// </summary>
        private void RaiseUpdateEvent()
        {
            // Raise the event by using the () operator.
            if (CCUpdateEvent != null)
                CCUpdateEvent(this, new CCUpdateEventArgs(_textRunList));

            _currentTextRun = DefaultCC608Run();
            _textRunList.Clear();
            _textRunList.Add(_currentTextRun);
        }

        /// <summary>
        /// Constructor. 
        /// </summary>
        public CC608Parser()
        {
            _currentTextRun = DefaultCC608Run();
            _textRunList.Add(_currentTextRun);
        }

        /// <summary>
        /// Parses a byte array of 608 CC data. 
        /// </summary>
        /// <param name="data"></param>
        public void Parse(byte[] data)
        {
            // The 608/708 data blob should start with GA94 signature
            if (data[0] != 'G' || data[1] != 'A' || data[2] != '9' || data[3] != '4')
                return;

            // The CC data blob should be at leasst 6 bytes as the 5th element contains 
            // the blob size. 
            if (data.Length < 6 || data[4] != 0x03)
                return;

            int ccPacketCount = data[5] & 0x1f;

            // Each CC packet is 3 bytes, and the actual packets should start at offset 8. 
            if (ccPacketCount * 3 + 8 > data.Length)
                return;

            // The actual CC packets start at offset 8
            int index = 7;

            for (int count = 0; count < ccPacketCount; count++)
            {
                byte ccFlag = data[index];
                byte data1 = data[index + 1];
                byte data2 = data[index + 2];

                bool ccIsValid = ((ccFlag & 0x04) == 0x04);
                int ccType = ccFlag & 0x03;

                // The CC type 0x00 is the type id for 608 data. 
                if (ccIsValid && ccType == 0x00)
                {
                    DecodeBytePair(data1, data2);
                }

                index += 3;
            }
        }

        /// <summary>
        /// Flag that indicates if the incoming closed caption data could be a repeat of 
        /// past two bytes. 
        /// </summary>
        bool _expectRepeat = false;

        /// <summary>
        /// The first byte of the last CC byte-pair received 
        /// </summary>
        byte _lastByte1 = 0;

        /// <summary>
        /// The second byte of the last CC byte-pair received 
        /// </summary>
        byte _lastByte2 = 0;

        /// <summary>
        /// The 608 control code types
        /// </summary>
        private enum CC608_CONTROLCODE
        {
            CC608_CONTROLCODE_Invalid = 0,
            CC608_CONTROLCODE_PAC,
            CC608_CONTROLCODE_MidRow,
            CC608_CONTROLCODE_MiscControl
        };

        /// <summary>
        /// Decodes a data byte pair that is parsed from a 608 packet. 
        /// </summary>
        /// <param name="first"></param>
        /// <param name="second"></param>
        private void DecodeBytePair(byte first, byte second)
        {
            CC608_CONTROLCODE codeType = GetControlCode(first, second);

            if (CC608_CONTROLCODE.CC608_CONTROLCODE_Invalid != codeType)
            {
                // It's a control code (PAC / Mid row code / misc control code)
                ProcessControlCode(codeType, first, second);
            }
            else if (IsSpecialChar(first, second))
            {
                // It's a special char represented by the second char
                ProcessSpecialChar(first, second);
            }
            else
            {
                // Check if this is a repeat of the last two characters, and if so ignore it
                if (_expectRepeat)
                {
                    if (_lastByte1 == (first & 0x7F) && _lastByte2 == (second & 0x7F))
                    {
                        // Got 2nd transmission of the same char; reset flag and ignore bytepair
                        _expectRepeat = false;
                        return;
                    }
                }
                else  
                {
                    _expectRepeat = true;
                }

                //  Store the bytes received after their parity bit is stripped.
                _lastByte1 = (byte)(first & 0x7F);
                _lastByte2 = (byte)(second & 0x7F);

                // If the 1st byte is in [0, F] then ignore 1st byte and print 2nd byte
                // as just a printable char
                if (!((first & 0x7F) >= 0x00 && (first & 0x7F) <= 0x0F))
                {
                    ProcessPrintableChar(first);
                }

                ProcessPrintableChar(second);
            }
        }


        /// <summary>
        /// Table of special char unicode values 
        /// </summary>
        static private short[] _specialCharactersTable = {  0x00ae,    0x00b0,    0x00bd,    0x00bf,    0x2122,    0x00a2,    0x00a3,    0x266b,
                                                            // 30h,       31h,       32h,       33h,       34h,       35h,       36h,       37h,
                                                            0x00e0,    0x0000,    0x00e8,    0x00e2,    0x00ea,    0x00ee,    0x00f4,    0x00fb };
                                                            // 38h,       39h,       3Ah,       3Bh,       3Ch,       3Dh,       3Eh,       3Fh 

        /// <summary>
        /// Processes a special character.
        /// </summary>
        /// <param name="first"></param>
        /// <param name="second"></param>
        /// <returns></returns>
        bool ProcessSpecialChar(byte first, byte second)
        {
            // Check if it's a repeat of the last special characters, and if so ignore it 
            if (_expectRepeat)
            {
                if (_lastByte1 == (first & 0x7F) && _lastByte2 == (second & 0x7F))
                {
                    // Got 2nd transmission of the same special character; reset flag and ignore bytepair
                    _expectRepeat = false;
                    return true;
                }
            }
            else
            {
                _expectRepeat = true;
            }

            //  Store the bytes received after their parity bit is stripped.
            _lastByte1 = (byte)(first & 0x7F);
            _lastByte2 = (byte)(second & 0x7F);

            if (!ValidParity(second))
            {
                // put special char solid block (7F)
                ProcessPrintableChar(0x7F);
            }
            else if ((second & 0x7F) >= 0x30 && (second & 0x7F) <= 0x3F)
            {
                int specailCharIndex = (second & 0x7F) - 0x30;
                _currentTextRun.Text += _specialCharactersTable[specailCharIndex];
            }
            else
            {
                // put special char solid block (7F)
                ProcessPrintableChar(0x7F);  
            }

            return true;
        }


        /// <summary>
        /// Processes a 608 control code
        /// </summary>
        /// <param name="eCodeType"></param>
        /// <param name="first"></param>
        /// <param name="second"></param>
        /// <returns></returns>
        bool ProcessControlCode(CC608_CONTROLCODE eCodeType, byte first, byte second)
        {
            // Make sure that the pair has valid parity bits
            if (!ValidParity(second))
                return false;


            bool success = true;
            if (!ValidParity(first))
            {
                if (_expectRepeat)  
                {
                    if (second == _lastByte2)
                    {
                        // this is a re-transmission -- ignore it
                    }
                    else   
                    {
                        success = ProcessPrintableChar(second);
                    }

                    _expectRepeat = false;
                }
                else  
                {
                    success = ProcessPrintableChar(0x7F) && ProcessPrintableChar(second);
                }

                return success;
            }

            // Check if it's a repeat of the last control code. If so ignore it.
            if (_expectRepeat)
            {
                if (_lastByte1 == first  && _lastByte2 == second)
                {
                    // Got 2nd transmission of the control code; reset flag and ignore bytepair
                    _expectRepeat = false;
                    return true;
                }
            }
            else  
            {
                _expectRepeat = true;
            }

            //  We store the bytes *before* the parity bit is stripped.
            _lastByte1 = first;
            _lastByte2 = second;

            first = (byte)(first & 0x7F);
            second = (byte)(second & 0x7F);

            switch (eCodeType)
            {
                case CC608_CONTROLCODE.CC608_CONTROLCODE_PAC:
                    return DecodePAC(first, second);

                case CC608_CONTROLCODE.CC608_CONTROLCODE_MidRow:
                    // TODO Processing of Mid row control codes are not implemented. 
                    // return DecodeMidRowCode(first, second) ;
                    return false;

                case CC608_CONTROLCODE.CC608_CONTROLCODE_MiscControl:
                    return DecodeMiscControlCode(first, second);

                default:
                    return false;  
            }
        }

        static private Color[] CC608ColorCodeToColor = { Colors.White, Colors.Green, Colors.Blue, Colors.Cyan, Colors.Red, Colors.Yellow, Colors.Magenta, Colors.Black };
        static int[] _PACtoRowMap = { 11, 1, 3, 12, 14, 5, 7, 9, 11, 1, 3, 12, 14, 5, 7, 9 };
        int _currentRow = 0;

        /// <summary>
        /// Decodes a "Preamble Address Code" (PAC) 
        /// </summary>
        /// <param name="first"></param>
        /// <param name="second"></param>
        /// <returns></returns>
        bool DecodePAC(byte first, byte second)
        {
            byte diff;
            int group = 0;

            // Turn off parity checking for PAC codec
            first &= 0x7F;
            second &= 0x7F;


            if (second >= 0x40 && second <= 0x5F)
            {
                group = 0;
                diff = (byte)(second - 0x40);
            }
            else if (second >= 0x60 && second <= 0x7F)
            {
                group = 1;
                diff = (byte)(second - 0x60);
            }
            else   // invalid 2nd byte for PAC
            {
                return false;
            }

            if (first < 0x10 || first > 0x1F)
            {
                return false;
            }

            // the row number is 1 more if the 2nd byte is in the 60-7F group
            int row = _PACtoRowMap[first - 0x10] + group;

            bool isUnderlined = ((diff & 0x01) == 0x01);

            if (diff <= 0x0D)  // color specification
            {
                _currentTextRun = DefaultCC608Run();

                Debug.Assert((diff >> 1) < CC608ColorCodeToColor.Length);
                
                _currentTextRun.Foreground = new SolidColorBrush(CC608ColorCodeToColor[diff >> 1]);
                
                if (isUnderlined)
                    _currentTextRun.TextDecorations = TextDecorations.Underline; 
                
                _textRunList.Add(_currentTextRun); 
            }
            else if (diff <= 0x0F)  // 0E, 0F == italics specification
            {
                _currentTextRun = DefaultCC608Run();
                
                _currentTextRun.FontStyle = FontStyles.Italic;
                
                if (isUnderlined)
                    _currentTextRun.TextDecorations = TextDecorations.Underline; 
                
                _textRunList.Add(_currentTextRun); 
            }
            else  // 10 -> 1F == indent specification
            {
                if (isUnderlined)
                {
                    _currentTextRun = DefaultCC608Run();
                    _currentTextRun.TextDecorations = TextDecorations.Underline;
                    _textRunList.Add(_currentTextRun);
                }

                while (_currentRow < row)
                {
                    _currentTextRun.Text += Environment.NewLine;
                    _currentRow++;
                }

                int indent = ((diff - 0x10) & 0xFE) << 1;

                for (int i = 0; i < indent; ++i)
                    _currentTextRun.Text += " ";
            }

            return true;
        }

        /// <summary>
        /// Decodes a "608 miscellanous" control code 
        /// </summary>
        /// <param name="first"></param>
        /// <param name="second"></param>
        /// <returns></returns>
        bool DecodeMiscControlCode(byte first, byte second)
        {
            bool result = false;

            switch (first)
            {
                case 0x14:      
                case 0x1C:      
                    switch (second)
                    {
                        case 0x20:   // TODO Not implemented: RCL: Resume Caption Loading
                            // result = HandleRCL(first, second);
                            break;

                        case 0x21:   // BS:  Backspace
                            result = HandleBS(first, second);
                            break;

                        case 0x22:   // AOF: reserved
                        case 0x23:   // AOF: reserved
                            return true;  // just ignore it

                        case 0x24:   // TODO Not implemented: DER: Delete to End of Row
                            // result = HandleDER(first, second) ;
                            break;

                        
                        case 0x25:   // RU2: Roll-Up Captions - 2 rows
                        case 0x26:   // RU3: Roll-Up Captions - 3 rows
                        case 0x27:   // RU4: Roll-Up Captions - 4 rows
                            result = HandleRU(first, second, 2 + second - 0x25);
                            break;

                        case 0x28:   // TODO Not implemented:  FON: Flash On
                            // result = HandleFON(first, second) ;
                            break;

                        case 0x29:   // TODO Not implemented: RDC: Resume Direct Captioning
                            // result = HandleRDC(first, second) ;

                            break;

                        case 0x2A:   // TODO Not implemented: TR:  Text Restart
                            // result = HandleTR(first, second) ;
                            break;

                        case 0x2B:   // TODO Not implemented: RTD: Resume Text Display
                            // result = HandleRTD(first, second) ;
                            break;

                        case 0x2C:   // EDM: Erase Displayed Memory
                            result = HandleEDM(first, second);
                            break;

                        case 0x2D:   // CR:  Carriage Return
                            result = HandleCR(first, second);
                            break;

                        case 0x2E:   // TODO Not implemented: ENM: Erase Non-displayed Memory
                            // result = HandleENM(first, second) ;
                            break;

                        case 0x2F:   // EOC: End of Caption 
                            result = HandleEOC(first, second);
                            break;

                        default:
                            return false;
                    }  
                    break;

                case 0x17:
                case 0x1F:
                    switch (second)
                    {
                        case 0x21:   // TO1: Tab Offset 1 column
                        case 0x22:   // TO2: Tab Offset 2 columns
                        case 0x23:   // TO3: Tab Offset 3 columns
                            // TODO Not implemented: 
                            // result = HandleTO(first, second, 1 + second - 0x21) ;
                            break;

                        default:
                            return false;
                    } 
                    break;

                default:
                    return false;
            }  

            return result;  
        }

        /// <summary>
        /// Handle "End Of Caption" command
        /// </summary>
        /// <param name="first"></param>
        /// <param name="second"></param>
        /// <returns></returns>
        bool HandleEOC(byte first, byte second)
        {
            RaiseUpdateEvent();
            return true;
        }

        /// <summary>
        /// Handle "Roll up" command
        /// </summary>
        /// <param name="first"></param>
        /// <param name="second"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        bool HandleRU(byte first, byte second, int count)
        {
            // treat roll up as a series of CR 
            for (int i = 0; i < count; ++i)
                HandleCR(first, second);

            return true;
        }

        /// <summary>
        /// Handle "Carriage Return" command
        /// </summary>
        /// <param name="first"></param>
        /// <param name="second"></param>
        /// <returns></returns>
        bool HandleCR(byte first, byte second)
        {
            _currentTextRun.Text += Environment.NewLine;

            return true;
        }

        /// <summary>
        /// Handle "Erase Displayed Memory" commands
        /// </summary>
        /// <param name="first"></param>
        /// <param name="second"></param>
        /// <returns></returns>
        bool HandleEDM(byte first, byte second)
        {
            return true;
        }

        /// <summary>
        /// Handle "Back Space" command
        /// </summary>
        /// <param name="first"></param>
        /// <param name="second"></param>
        /// <returns></returns>
        bool HandleBS(byte first, byte second)
        {
            if (_currentTextRun.Text.Length > 0)
                _currentTextRun.Text.Remove(_currentTextRun.Text.Length - 1);

            return true;

        }

        bool IsStandardChar(byte ch)
        {
            return (ch >= 0x20 && ch <= 0x7F);
        }

        bool ValidParity(byte ch)
        {
            ch ^= (byte)(ch >> 4);
            ch ^= (byte)(ch >> 2);
            return (0 != (0x01 & (ch ^ (ch >> 1))));
        }

        char MAKECCCHAR(byte b1, byte b2)
        {
            return (char)((b1) << 8 | (b2));
        }

        bool ProcessPrintableChar(byte ch)
        {
            if (ch == 0x80)
                ch = 0x20;

            if (!IsStandardChar((byte)(ch & 0x7F)))
            {
                return false;
            }

            // if a printable char doesn't have valid parity, then replace it with 7Fh.
            if (!ValidParity(ch))  
            {
                ch = 0x7F;            
            }

            char c;
            switch (ch & 0x7F)  // we only look at the parity-less bits
            {
                case 0x2A:  // lower-case a with acute accent
                    c = MAKECCCHAR(0, 0x61); // use a instead
                    break;

                case 0x5C:  // lower-case e with acute accent
                    c = MAKECCCHAR(0, 0x65); // use e instead
                    break;

                case 0x5E:  // lower-case i with acute accent
                    c = MAKECCCHAR(0, 0x69); // use i instead
                    break;

                case 0x5F:  // lower-case o with acute accent
                    c = MAKECCCHAR(0, 0x6F); // use o instead
                    break;

                case 0x60:  // lower-case u with acute accent
                    c = MAKECCCHAR(0, 0x75); // use u instead
                    break;

                case 0x7B:  // lower-case c with cedilla
                    c = MAKECCCHAR(0, 0x63); // use c instead
                    break;

                case 0x7C:  // division sign
                    c = MAKECCCHAR(0, 0x7C); // use | instead
                    break;

                case 0x7D:  // upper-case N with tilde
                    c = MAKECCCHAR(0, 0x4E); // use N instead
                    break;

                case 0x7E:  // lower-case n with tilde
                    c = MAKECCCHAR(0, 0x6E); // use n instead
                    break;

                case 0x7F:  // solid block
                    c = MAKECCCHAR(0, 0x20); // use space instead
                    break;

                default:
                    c = MAKECCCHAR(0, (byte)(ch & 0x7F)); 
                    break;
            }

            _currentTextRun.Text += c;
            return true;
        }


        CC608_CONTROLCODE GetControlCode(byte first, byte second)
        {
            if (IsPAC(first, second))
                return CC608_CONTROLCODE.CC608_CONTROLCODE_PAC;

            if (IsMidRowCode(first, second))
                return CC608_CONTROLCODE.CC608_CONTROLCODE_MidRow;

            if (IsMiscControlCode(first, second))
                return CC608_CONTROLCODE.CC608_CONTROLCODE_MiscControl;

            return CC608_CONTROLCODE.CC608_CONTROLCODE_Invalid;
        }


        bool IsSpecialChar(byte first, byte second)
        {
            first &= 0x7F;
            second &= 0x7F;

            if (0x11 == first && (0x30 <= second && 0x3f >= second))
                return true;

            if (0x19 == first && (0x30 <= second && 0x3f >= second))
                return true;

            return false;
        }

        bool IsPAC(byte first, byte second)
        {
            first &= 0x7F;
            second &= 0x7F;

            if ((0x10 <= first && 0x17 >= first) && (0x40 <= second && 0x7F >= second))
                return true;

            if ((0x18 <= first && 0x1F >= first) && (0x40 <= second && 0x7F >= second))
                return true;

            return false;
        }


        bool IsMiscControlCode(byte first, byte second)
        {
            first &= 0x7F;
            second &= 0x7F;

            if ((0x21 <= second && 0x23 >= second) && (0x17 == first || 0x1F == first))
                return true;

            if ((0x14 == first || 0x15 == first) && (0x20 <= second && 0x2F >= second))
                return true;

            if ((0x1C == first || 0x1D == first) && (0x20 <= second && 0x2F >= second))
                return true;

            return false;
        }

        bool IsMidRowCode(byte first, byte second)
        {
            first &= 0x7F;
            second &= 0x7F;

            if ((0x11 == first) && (0x20 <= second && 0x2F >= second))
                return true;

            if ((0x19 == first) && (0x20 <= second && 0x2F >= second))
                return true;

            return false;
        }

 
    }
}
