//#define PRINT_DEBUG

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Management;
using System.Windows.Forms;
using System.Net.Sockets;
using System.Net;

// Basic BLE listener for the Intel Curie based Lighthouse tracker; based on  
// sample code from the BGLib library.

// Geva Patz 2016

namespace LighthouseTracker {
    public partial class Form1 : Form {
        public Bluegiga.BGLib bglib = new Bluegiga.BGLib();
        public Boolean isAttached = false;
        public Dictionary<string, string> portDict = new Dictionary<string, string>();


        public const UInt16 STATE_STANDBY = 0;
        public const UInt16 STATE_SCANNING = 1;
        public const UInt16 STATE_CONNECTING = 2;
        public const UInt16 STATE_FINDING_SERVICES = 3;
        public const UInt16 STATE_FINDING_ATTRIBUTES = 4;
        public const UInt16 STATE_LISTENING_MEASUREMENTS = 5;

        public UInt16 app_state = STATE_STANDBY;
        public Byte connection_handle = 0;
        public UInt16 att_handlesearch_start = 0;
        public UInt16 att_handlesearch_end = 0;
        public UInt16 att_handle_measurement = 0;
        public UInt16 att_handle_measurement_ccc = 0;

        public void GAPScanResponseEvent(object sender, Bluegiga.BLE.Events.GAP.ScanResponseEventArgs e) {
#if PRINT_DEBUG
            String log = String.Format("ble_evt_gap_scan_response: rssi={0}, packet_type={1}, sender=[ {2}], address_type={3}, bond={4}, data=[ {5}]" + Environment.NewLine,
                (SByte)e.rssi,
                e.packet_type,
                ByteArrayToHexString(e.sender),
                e.address_type,
                e.bond,
                ByteArrayToHexString(e.data)
                );
            Console.Write(log);
            ThreadSafeDelegate(delegate { txtLog.AppendText(log); });
#endif
            // pull all advertised service info from ad packet
            List<Byte[]> ad_services = new List<Byte[]>();
            Byte[] this_field = { };
            int bytes_left = 0;
            int field_offset = 0;
            for (int i = 0; i < e.data.Length; i++) {
                if (bytes_left == 0) {
                    bytes_left = e.data[i];
                    this_field = new Byte[e.data[i]];
                    field_offset = i + 1;
                } else {
                    this_field[i - field_offset] = e.data[i];
                    bytes_left--;
                    if (bytes_left == 0) {
                        if (this_field[0] == 0x02 || this_field[0] == 0x03) {
                            // partial or complete list of 16-bit UUIDs
                            ad_services.Add(this_field.Skip(1).Take(2).Reverse().ToArray());
                        } else if (this_field[0] == 0x04 || this_field[0] == 0x05) {
                            // partial or complete list of 32-bit UUIDs
                            ad_services.Add(this_field.Skip(1).Take(4).Reverse().ToArray());
                        } else if (this_field[0] == 0x06 || this_field[0] == 0x07) {
                            // partial or complete list of 128-bit UUIDs
                            ad_services.Add(this_field.Skip(1).Take(16).Reverse().ToArray());
                        }
                    }
                }
            }

            if (ad_services.Any(a => a.SequenceEqual((new Byte[] { 0x19, 0xb1, 0x00, 0x10, 0xE8, 0xF2,
                    0x53, 0x7E, 0x4F, 0x6C, 0xD1, 0x04, 0x76, 0x8A, 0x01, 0x16 })/*.Reverse()*/))) {
                // connect to this device
                Byte[] cmd = bglib.BLECommandGAPConnectDirect(e.sender, e.address_type, 0x06, 0x0C, 0x100, 0); // ~120/60 Hz min/max interval
#if PRINT_DEBUG
                ThreadSafeDelegate(delegate { txtLog.AppendText(String.Format("=> TX ({0}) [ {1}]", cmd.Length, ByteArrayToHexString(cmd)) + Environment.NewLine); });
#endif
                bglib.SendCommand(serialAPI, cmd);
                
                // update state
                app_state = STATE_CONNECTING;
            }
        }

        // the "connection_status" event occurs when a new connection is established
        public void ConnectionStatusEvent(object sender, Bluegiga.BLE.Events.Connection.StatusEventArgs e) {
            String log = String.Format("ble_evt_connection_status: connection={0}, flags={1}, address=[ {2}], address_type={3}, conn_interval={4}, timeout={5}, latency={6}, bonding={7}" + Environment.NewLine,
                e.connection,
                e.flags,
                ByteArrayToHexString(e.address),
                e.address_type,
                e.conn_interval,
                e.timeout,
                e.latency,
                e.bonding
                );
            Console.Write(log);
            ThreadSafeDelegate(delegate { txtLog.AppendText(log); });

            if ((e.flags & 0x05) == 0x05) {
                // connected, now perform service discovery
                connection_handle = e.connection;
                ThreadSafeDelegate(delegate { txtLog.AppendText(String.Format("Connected to {0}", ByteArrayToHexString(e.address)) + Environment.NewLine); });
                Byte[] cmd = bglib.BLECommandATTClientReadByGroupType(e.connection, 0x0001, 0xFFFF, new Byte[] { 0x00, 0x28 }); // "service" UUID is 0x2800 (little-endian for UUID uint8array)

#if PRINT_DEBUG
                ThreadSafeDelegate(delegate { txtLog.AppendText(String.Format("=> TX ({0}) [ {1}]", cmd.Length, ByteArrayToHexString(cmd)) + Environment.NewLine); });
#endif
                bglib.SendCommand(serialAPI, cmd);

                // update state
                app_state = STATE_FINDING_SERVICES;
            }
        }

        public void ATTClientGroupFoundEvent(object sender, Bluegiga.BLE.Events.ATTClient.GroupFoundEventArgs e) {
            String log = String.Format("ble_evt_attclient_group_found: connection={0}, start={1}, end={2}, uuid=[ {3}]" + Environment.NewLine,
                e.connection,
                e.start,
                e.end,
                ByteArrayToHexString(e.uuid)
                );
            Console.Write(log);
            ThreadSafeDelegate(delegate { txtLog.AppendText(log); });

            if (e.uuid.SequenceEqual((new Byte[] { 0x19, 0xb1, 0x00, 0x10, 0xE8, 0xF2,
                    0x53, 0x7E, 0x4F, 0x6C, 0xD1, 0x04, 0x76, 0x8A, 0x01, 0x16 }).Reverse())) {
                ThreadSafeDelegate(delegate { txtLog.AppendText(String.Format("Found attribute group for service w/UUID=0x180D: start={0}, end=%d", e.start, e.end) + Environment.NewLine); });
                att_handlesearch_start = e.start;
                att_handlesearch_end = e.end;
            }
        }

        public void ATTClientFindInformationFoundEvent(object sender, Bluegiga.BLE.Events.ATTClient.FindInformationFoundEventArgs e) {
            String log = String.Format("ble_evt_attclient_find_information_found: connection={0}, chrhandle={1}, uuid=[ {2}]" + Environment.NewLine,
                e.connection,
                e.chrhandle,
                ByteArrayToHexString(e.uuid)
                );
            Console.Write(log);
            ThreadSafeDelegate(delegate { txtLog.AppendText(log); });

            if (e.uuid.SequenceEqual((new Byte[] { 0x19, 0xb1, 0x00, 0x12, 0xE8, 0xF2,
                    0x53, 0x7E, 0x4F, 0x6C, 0xD1, 0x04, 0x76, 0x8A, 0x01, 0x16 }).Reverse())) {
                ThreadSafeDelegate(delegate { txtLog.AppendText(String.Format("Found Lighthouse service: handle={0}", e.chrhandle) + Environment.NewLine); });
                att_handle_measurement = e.chrhandle;
            }
            else if (e.uuid.SequenceEqual((new Byte[] { 0x19, 0xb1, 0x00, 0x14, 0xE8, 0xF2,
                    0x53, 0x7E, 0x4F, 0x6C, 0xD1, 0x04, 0x76, 0x8A, 0x01, 0x16 }).Reverse()) && att_handle_measurement > 0) {
                ThreadSafeDelegate(delegate { txtLog.AppendText(String.Format("Found IMU service: handle={0}", e.chrhandle) + Environment.NewLine); });
                att_handle_measurement_ccc = e.chrhandle;
            }
        }

        public void ATTClientProcedureCompletedEvent(object sender, Bluegiga.BLE.Events.ATTClient.ProcedureCompletedEventArgs e) {
            String log = String.Format("ble_evt_attclient_procedure_completed: connection={0}, result={1}, chrhandle={2}" + Environment.NewLine,
                e.connection,
                e.result,
                e.chrhandle
                );
            Console.Write(log);
            ThreadSafeDelegate(delegate { txtLog.AppendText(log); });

            if (app_state == STATE_FINDING_SERVICES) {
                if (att_handlesearch_end > 0) {
               
                    Byte[] cmd = bglib.BLECommandATTClientFindInformation(e.connection, att_handlesearch_start, att_handlesearch_end);
#if PRINT_DEBUG
                    ThreadSafeDelegate(delegate { txtLog.AppendText(String.Format("=> TX ({0}) [ {1}]", cmd.Length, ByteArrayToHexString(cmd)) + Environment.NewLine); });
#endif
                    bglib.SendCommand(serialAPI, cmd);

                    // update state
                    app_state = STATE_FINDING_ATTRIBUTES;
                } else {
                    ThreadSafeDelegate(delegate { txtLog.AppendText("Could not find service" + Environment.NewLine); });
                }
            }
            else if (app_state == STATE_FINDING_ATTRIBUTES) {
                if (att_handle_measurement_ccc > 0) {
                    Byte[] cmd = bglib.BLECommandATTClientAttributeWrite(e.connection, (UInt16)(att_handle_measurement + 0x01), new Byte[] { 0x01, 0x00 });
#if PRINT_DEBUG
                    ThreadSafeDelegate(delegate { txtLog.AppendText(String.Format("=> TX ({0}) [ {1}]", cmd.Length, ByteArrayToHexString(cmd)) + Environment.NewLine); });
#endif
                    bglib.SendCommand(serialAPI, cmd);
                    Byte[] cmd2 = bglib.BLECommandATTClientAttributeWrite(e.connection, (UInt16)(att_handle_measurement_ccc + 0x01), new Byte[] { 0x01, 0x00 });
#if PRINT_DEBUG
                    ThreadSafeDelegate(delegate { txtLog.AppendText(String.Format("=> TX ({0}) [ {1}]", cmd.Length, ByteArrayToHexString(cmd)) + Environment.NewLine); });
#endif
                    bglib.SendCommand(serialAPI, cmd2);
                    app_state = STATE_LISTENING_MEASUREMENTS;
                } else {
                    ThreadSafeDelegate(delegate { txtLog.AppendText("Could not find 'Heart Rate' measurement attribute with UUID 0x2A37" + Environment.NewLine); });
                }
            }
        }

        private int[] fails = { 0, 0, 0, 0, 0, 0 };
        int MAXFAILS = 10;
        bool do_b = false;
        string b_text;
        float factor = 2222.2f;

        public void ATTClientAttributeValueEvent(object sender, Bluegiga.BLE.Events.ATTClient.AttributeValueEventArgs e) {

            if (e.connection == connection_handle && e.atthandle == att_handle_measurement) {
                UInt32[] times = new UInt32[6];
                UInt16 status;
                byte[] vals = e.value.Reverse().ToArray();
                for (int i = 0; i < 6; ++i) {
                    times[i] = (BitConverter.ToUInt32(vals, ((5 - i) * 3) + 1)) >> 8;
                }
                status = BitConverter.ToUInt16(vals, 0);
                int is_b = (status & 0xf000) >> 15;
                for (int i = 0; i < 3; ++i) {
                    if ((status & (1 << i)) == 0)
                        ++fails[(1 - is_b) * 3 + i];
                    else
                        fails[(1 - is_b) * 3 + i] = 0;
                }
                if (is_b != 0) {
                    this.Invoke(new Action(delegate () {
                        this.textBox1.Text = String.Format("{0:#}", ((float)times[0]) / factor);
                        this.textBox1.BackColor = fails[0] < MAXFAILS ? Color.FromArgb(0x33, 0xff, 0x99) : Color.FromArgb(0xFF, 0x99, 0x99);
                    }));
                    this.Invoke(new Action(delegate () {
                        this.textBox2.Text = String.Format("{0:#}", ((float)times[1]) / factor);
                        this.textBox2.BackColor = fails[0] < MAXFAILS ? Color.FromArgb(0x33, 0xff, 0x99) : Color.FromArgb(0xFF, 0x99, 0x99);
                    }));
                    this.Invoke(new Action(delegate () {
                        this.textBox4.Text = String.Format("{0:#}", ((float)times[2]) / factor);
                        this.textBox4.BackColor = fails[1] < MAXFAILS ? Color.FromArgb(0x33, 0xff, 0x99) : Color.FromArgb(0xFF, 0x99, 0x99);
                    }));
                    this.Invoke(new Action(delegate () {
                        this.textBox3.Text = String.Format("{0:#}", ((float)times[3]) / factor);
                        this.textBox3.BackColor = fails[1] < MAXFAILS ? Color.FromArgb(0x33, 0xff, 0x99) : Color.FromArgb(0xFF, 0x99, 0x99);
                    }));
                    this.Invoke(new Action(delegate () {
                        this.textBox6.Text = String.Format("{0:#}", ((float)times[4]) / factor);
                        this.textBox6.BackColor = fails[2] < MAXFAILS ? Color.FromArgb(0x33, 0xff, 0x99) : Color.FromArgb(0xFF, 0x99, 0x99);
                    }));
                    this.Invoke(new Action(delegate () {
                        this.textBox5.Text = String.Format("{0:#}", ((float)times[5]) / factor);
                        this.textBox5.BackColor = fails[2] < MAXFAILS ? Color.FromArgb(0x33, 0xff, 0x99) : Color.FromArgb(0xFF, 0x99, 0x99);
                    }));
                } else {
                    this.Invoke(new Action(delegate () {
                        this.textBox12.Text = String.Format("{0:#}", ((float)times[0]) / factor);
                        this.textBox12.BackColor = fails[3] < MAXFAILS ? Color.FromArgb(0x33, 0xff, 0x99) : Color.FromArgb(0xFF, 0x99, 0x99);
                    }));
                    this.Invoke(new Action(delegate () {
                        this.textBox11.Text = String.Format("{0:#}", ((float)times[1]) / factor);
                        this.textBox11.BackColor = fails[3] < MAXFAILS ? Color.FromArgb(0x33, 0xff, 0x99) : Color.FromArgb(0xFF, 0x99, 0x99);
                    }));
                    this.Invoke(new Action(delegate () {
                        this.textBox10.Text = String.Format("{0:#}", ((float)times[2]) / factor);
                        this.textBox10.BackColor = fails[4] < MAXFAILS ? Color.FromArgb(0x33, 0xff, 0x99) : Color.FromArgb(0xFF, 0x99, 0x99);
                    }));
                    this.Invoke(new Action(delegate () {
                        this.textBox9.Text = String.Format("{0:#}", ((float)times[3]) / factor);
                        this.textBox9.BackColor = fails[4] < MAXFAILS ? Color.FromArgb(0x33, 0xff, 0x99) : Color.FromArgb(0xFF, 0x99, 0x99);
                    }));
                    this.Invoke(new Action(delegate () {
                        this.textBox8.Text = String.Format("{0:#}", ((float)times[4]) / factor);
                        this.textBox8.BackColor = fails[5] < MAXFAILS ? Color.FromArgb(0x33, 0xff, 0x99) : Color.FromArgb(0xFF, 0x99, 0x99);
                    }));
                    this.Invoke(new Action(delegate () {
                        this.textBox7.Text = String.Format("{0:#}", ((float)times[5]) / factor);
                        this.textBox7.BackColor = fails[5] < MAXFAILS ? Color.FromArgb(0x33, 0xff, 0x99) : Color.FromArgb(0xFF, 0x99, 0x99);
                    }));
                }
#if PRINT_DEBUG
                ThreadSafeDelegate(delegate { txtLog.AppendText(String.Format("Time {0}: {1:X} = {2:#.##} uS, {3:#.##} deg", 0, times[0], ((float)times[0]) / 48.0, ((float)times[0]) / factor) + Environment.NewLine); });
                ThreadSafeDelegate(delegate { txtLog.AppendText(String.Format("Time {0}: {1:X} = {2:#.##} uS, {3:#.##} deg", 1, times[1], ((float)times[1]) / 48.0, ((float)times[1]) / factor) + Environment.NewLine); });
                ThreadSafeDelegate(delegate { txtLog.AppendText(String.Format("Time {0}: {1:X} = {2:#.##} uS, {3:#.##} deg", 2, times[2], ((float)times[2]) / 48.0, ((float)times[2]) / factor) + Environment.NewLine); });
                ThreadSafeDelegate(delegate { txtLog.AppendText(String.Format("Time {0}: {1:X} = {2:#.##} uS, {3:#.##} deg", 3, times[3], ((float)times[3]) / 48.0, ((float)times[3]) / factor) + Environment.NewLine); });
                ThreadSafeDelegate(delegate { txtLog.AppendText(String.Format("Time {0}: {1:X} = {2:#.##} uS, {3:#.##} deg", 4, times[4], ((float)times[4]) / 48.0, ((float)times[4]) / factor) + Environment.NewLine); });
                ThreadSafeDelegate(delegate { txtLog.AppendText(String.Format("Time {0}: {1:X} = {2:#.##} uS, {3:#.##} deg", 5, times[5], ((float)times[5]) / 48.0, ((float)times[5]) / factor) + Environment.NewLine); });
                ThreadSafeDelegate(delegate { txtLog.AppendText(String.Format("Status: {0:X}", status) + Environment.NewLine); });
#endif

                Socket sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                String text = "";
                bool doit = false;
                double angleH = 0f, angleV = 0f;
                if (is_b == 0 && do_b) {
                    if (fails[4] == 0) {
                        angleH = times[2];
                        angleV = times[3];
                        text = String.Format("{0:0.###} {1:0.###}", ((float)times[2]) / factor, ((float)times[3]) / factor);
                        doit = true;
                    } else if (fails[5] == 0) {
                        angleH = times[4];
                        angleV = times[5];
                        text = String.Format("{0:0.###} {1:0.###}", ((float)times[4]) / factor, ((float)times[5]) / factor);
                        doit = true;
                    } else if (fails[3] == 0) {
                        angleH = times[0];
                        angleV = times[1];
                        text = String.Format("{0:0.###} {1:0.###}", ((float)times[0]) / factor, ((float)times[1]) / factor);
                        doit = true;
                    }

                } else {
                    if (fails[1] == 0) {
                        angleH = times[2];
                        angleV = times[3];
                        b_text = String.Format(" {0:0.###} {1:0.###}", ((float)times[2]) / factor, ((float)times[3]) / factor);
                        do_b = true;
                    } else if (fails[2] == 0) {
                        angleH = times[4];
                        angleV = times[5];
                        b_text = String.Format(" {0:0.###} {1:0.###}", ((float)times[4]) / factor, ((float)times[5]) / factor);
                        do_b = true;
                    } else if (fails[0] == 0) {
                        angleH = times[0];
                        angleV = times[1];
                        b_text = String.Format(" {0:0.###} {1:0.###}", ((float)times[0]) / factor, ((float)times[1]) / factor);
                        do_b = true;
                    } else {
                        do_b = false;
                    }
                }
                if (doit) {
                    IPAddress addr = IPAddress.Parse("127.0.0.1");
                    text += b_text;
                    IPEndPoint ep = new IPEndPoint(addr, 8051);
                    byte[] send_buffer = Encoding.ASCII.GetBytes(text);
                    sock.SendTo(send_buffer, ep);
                }


            } else if (e.connection == connection_handle && e.atthandle == att_handle_measurement_ccc) {
                ThreadSafeDelegate(delegate { txtLog.AppendText(String.Format("Got IMU reading") + Environment.NewLine); });

            }
        }

        public void ThreadSafeDelegate(MethodInvoker method) {
            if (InvokeRequired)
                BeginInvoke(method);
            else
                method.Invoke();
        }

        public string ByteArrayToHexString(Byte[] ba) {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
                hex.AppendFormat("{0:x2} ", b);
            return hex.ToString();
        }

        private void DataReceivedHandler(
                                object sender,
                                System.IO.Ports.SerialDataReceivedEventArgs e) {
            System.IO.Ports.SerialPort sp = (System.IO.Ports.SerialPort)sender;
            Byte[] inData = new Byte[sp.BytesToRead];
            sp.Read(inData, 0, sp.BytesToRead);

#if PRINT_DEBUG
            // PRINT_DEBUG: display bytes read
            ThreadSafeDelegate(delegate { txtLog.AppendText(String.Format("<= RX ({0}) [ {1}]", inData.Length, ByteArrayToHexString(inData)) + Environment.NewLine); });
#endif
            for (int i = 0; i < inData.Length; i++) {
                bglib.Parse(inData[i]);
            }
        }

        public Form1() {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e) {
            // initialize list of ports
            btnRefresh_Click(sender, e);

            // initialize COM port combobox with list of ports
            comboPorts.DataSource = new BindingSource(portDict, null);
            comboPorts.DisplayMember = "Value";
            comboPorts.ValueMember = "Key";

            // initialize serial port with all of the normal values (should work with BLED112 on USB)
            serialAPI.Handshake = System.IO.Ports.Handshake.RequestToSend;
            serialAPI.BaudRate = 115200;
            serialAPI.DataBits = 8;
            serialAPI.StopBits = System.IO.Ports.StopBits.One;
            serialAPI.Parity = System.IO.Ports.Parity.None;
            serialAPI.DataReceived += new System.IO.Ports.SerialDataReceivedEventHandler(DataReceivedHandler);

            // initialize BGLib events we'll need for this script
            bglib.BLEEventGAPScanResponse += new Bluegiga.BLE.Events.GAP.ScanResponseEventHandler(this.GAPScanResponseEvent);
            bglib.BLEEventConnectionStatus += new Bluegiga.BLE.Events.Connection.StatusEventHandler(this.ConnectionStatusEvent);
            bglib.BLEEventATTClientGroupFound += new Bluegiga.BLE.Events.ATTClient.GroupFoundEventHandler(this.ATTClientGroupFoundEvent);
            bglib.BLEEventATTClientFindInformationFound += new Bluegiga.BLE.Events.ATTClient.FindInformationFoundEventHandler(this.ATTClientFindInformationFoundEvent);
            bglib.BLEEventATTClientProcedureCompleted += new Bluegiga.BLE.Events.ATTClient.ProcedureCompletedEventHandler(this.ATTClientProcedureCompletedEvent);
            bglib.BLEEventATTClientAttributeValue += new Bluegiga.BLE.Events.ATTClient.AttributeValueEventHandler(this.ATTClientAttributeValueEvent);
        }

        private void btnRefresh_Click(object sender, EventArgs e) {
            // get a list of all available ports on the system
            portDict.Clear();
            try {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_SerialPort");
                //string[] ports = System.IO.Ports.SerialPort.GetPortNames();
                foreach (ManagementObject queryObj in searcher.Get()) {
                    portDict.Add(String.Format("{0}", queryObj["DeviceID"]), String.Format("{0} - {1}", queryObj["DeviceID"], queryObj["Caption"]));
                }
            } catch (ManagementException ex) {
                portDict.Add("0", "Error " + ex.Message);
            }
        }

        private void btnAttach_Click(object sender, EventArgs e) {
            if (!isAttached) {
                txtLog.AppendText("Opening serial port '" + comboPorts.SelectedValue.ToString() + "'..." + Environment.NewLine);
                serialAPI.PortName = comboPorts.SelectedValue.ToString();
                serialAPI.Open();
                txtLog.AppendText("Port opened" + Environment.NewLine);
                isAttached = true;
                btnAttach.Text = "Detach";
                btnGo.Enabled = true;
                btnReset.Enabled = true;
            } else {
                txtLog.AppendText("Closing serial port..." + Environment.NewLine);
                serialAPI.Close();
                txtLog.AppendText("Port closed" + Environment.NewLine);
                isAttached = false;
                btnAttach.Text = "Attach";
                btnGo.Enabled = false;
                btnReset.Enabled = false;
            }
        }

        private void btnGo_Click(object sender, EventArgs e) {
            // start the scan/connect process now
            Byte[] cmd;

            // set scan parameters
            cmd = bglib.BLECommandGAPSetScanParameters(0xC8, 0xC8, 1); // 125ms interval, 125ms window, active scanning
            // PRINT_DEBUG: display bytes read
            ThreadSafeDelegate(delegate { txtLog.AppendText(String.Format("=> TX ({0}) [ {1}]", cmd.Length, ByteArrayToHexString(cmd)) + Environment.NewLine); });
            bglib.SendCommand(serialAPI, cmd);
            //while (bglib.IsBusy()) ;

            // begin scanning for BLE peripherals
            cmd = bglib.BLECommandGAPDiscover(1); // generic discovery mode
            // PRINT_DEBUG: display bytes read
            ThreadSafeDelegate(delegate { txtLog.AppendText(String.Format("=> TX ({0}) [ {1}]", cmd.Length, ByteArrayToHexString(cmd)) + Environment.NewLine); });
            bglib.SendCommand(serialAPI, cmd);
            //while (bglib.IsBusy()) ;

            // update state
            app_state = STATE_SCANNING;

            // disable "GO" button since we already started, and sending the same commands again sill not work right
            btnGo.Enabled = false;
        }

        private void btnReset_Click(object sender, EventArgs e) {
            // stop everything we're doing, if possible
            Byte[] cmd;

            // disconnect if connected
            cmd = bglib.BLECommandConnectionDisconnect(0);
#if PRINT_DEBUG
            // PRINT_DEBUG: display bytes read
            ThreadSafeDelegate(delegate { txtLog.AppendText(String.Format("=> TX ({0}) [ {1}]", cmd.Length, ByteArrayToHexString(cmd)) + Environment.NewLine); });
#endif
            bglib.SendCommand(serialAPI, cmd);
            //while (bglib.IsBusy()) ;

            // stop scanning if scanning
            cmd = bglib.BLECommandGAPEndProcedure();
#if PRINT_DEBUG
            // PRINT_DEBUG: display bytes read
            ThreadSafeDelegate(delegate { txtLog.AppendText(String.Format("=> TX ({0}) [ {1}]", cmd.Length, ByteArrayToHexString(cmd)) + Environment.NewLine); });
#endif
            bglib.SendCommand(serialAPI, cmd);
            //while (bglib.IsBusy()) ;

            // stop advertising if advertising
            cmd = bglib.BLECommandGAPSetMode(0, 0);
#if PRINT_DEBUG
            // PRINT_DEBUG: display bytes read
            ThreadSafeDelegate(delegate { txtLog.AppendText(String.Format("=> TX ({0}) [ {1}]", cmd.Length, ByteArrayToHexString(cmd)) + Environment.NewLine); });
#endif
            bglib.SendCommand(serialAPI, cmd);
            //while (bglib.IsBusy()) ;

            // enable "GO" button to allow them to start again
            btnGo.Enabled = true;

            // update state
            app_state = STATE_STANDBY;
        }

    }
}