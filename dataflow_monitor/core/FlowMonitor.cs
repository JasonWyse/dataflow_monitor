using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

/* 引入SharpPcap */
using SharpPcap;
using SharpPcap.LibPcap;
using SharpPcap.AirPcap;
using SharpPcap.WinPcap;
using PacketDotNet;

using System.Net.Sockets;
using System.Diagnostics;
using System.Timers;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace dataflow_monitor.core
{
    class FlowMonitor
    {
        #region Declare Variable 声明变量

        private static FlowMonitor backstageMonitor = null;
        public Form1 form = null;       


        // 变量声明
        enum ProtocolType { TCP, UDP };                             // 协议类型 ： TCP 和 UDP
        IPAddress hostIP;                                          // 本机IP地址        
        ArrayList packet_array = new ArrayList();                   // 临时存储包
        BackgroundWorker backgroundWorker1;                         // 监控专用线程
        System.Timers.Timer MainTimer = new System.Timers.Timer();  // 每秒对抓包数据进行分析的 Timer
        
        ArrayList TcpIP_array = new ArrayList();

        private Object thisLock = new Object();//多线程加锁
        #endregion
        /// <summary>
        /// 私有构建函数。
        /// </summary>
        private FlowMonitor()
        {
            Initialise();
        }      
        /// 返回后台监控。        
        public static FlowMonitor GetMonitor()
        {
            if (backstageMonitor == null)
                backstageMonitor = new FlowMonitor();            
            return backstageMonitor;
        }
        #region Function 函数
        /* ------------------ Function ~ ------------------ */
        private void Initialise()
        {
            // 获取本机IP地址
            hostIP = getHostIPAddress();

            // 新建线程监控。
            backgroundWorker1 = new System.ComponentModel.BackgroundWorker();
            backgroundWorker1.DoWork += backgroundWorker_DoWork;
            backgroundWorker1.RunWorkerAsync();
            
            // 每秒处理数据
            MainTimer.Interval = 1000;
            MainTimer.Elapsed += ManageDataTimerEvent;
            MainTimer.Enabled = false;
        }

        /// <summary>
        /// 通过判断IP地址是否在3类IP地址区间内进而得出是否为内网。
        /// 
        /// A类：10.0.0.0 ~ 10.255.255.255
        /// B类：172.16.0.0 ~ 172.31.255.255
        /// C类：192.168.0.0 ~ 192.168.255.255     
        bool IsInnerNet(IPAddress ip)
        {
            byte[] address_byte = ip.GetAddressBytes();

            bool a = IsInArea(10, 10, address_byte[0]) && IsInArea(0, 255, address_byte[1]) && IsInArea(0, 255, address_byte[2]) && IsInArea(0, 255, address_byte[3]);
            bool b = IsInArea(172, 172, address_byte[0]) && IsInArea(16, 31, address_byte[1]) && IsInArea(0, 255, address_byte[2]) && IsInArea(0, 255, address_byte[3]);
            bool c = IsInArea(192, 192, address_byte[0]) && IsInArea(168, 168, address_byte[1]) && IsInArea(0, 255, address_byte[2]) && IsInArea(0, 255, address_byte[3]);

            return (a || b || c || ip.Equals("127.0.0.1"));
        }
     
        /// 判断某数字是否在一个区间内。
        /// 即 i 属于 [a,b]     
        bool IsInArea(byte begin, byte end, byte i)
        {
            return (i >= begin && i <= end);
        }      
        /// 获取主机IP。   
        private IPAddress getHostIPAddress()
        {
            IPHostEntry host;
            IPAddress localIP = null;
            host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (IPAddress ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    localIP = ip;
                    break;
                }
            }
            return localIP;
        }         
       
        #endregion

        /// <summary>
        /// 监听事件。每当捕获到包时触发该事件。
        /// </summary>
        private void device_OnPacketArrival(object sender, CaptureEventArgs e)
        {
            lock (packet_array)
                packet_array.Add(e.Packet);
        }       
        /// 多线程运行，启动监控。
        ///     Refer to SharpPcap Example       
        private void backgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            // 延迟 1.5s 开启监控
            //System.Threading.Thread.Sleep(1500);

            try
            {               
                if (CaptureDeviceList.Instance.Count < 1)
                {
                    //MessageBox.Show("No devices were found on this machine");
                    return;
                }
                var devices = CaptureDeviceList.Instance;
                int readTimeoutMilliseconds = 1000;
                var device = CaptureDeviceList.Instance[1];

                /*
                 * 不开启混杂模式
                 */
                device.Open(DeviceMode.Normal, readTimeoutMilliseconds);

                device.OnPacketArrival +=
                    new PacketArrivalEventHandler(device_OnPacketArrival);

                // Open the device for capturing
                if (device is AirPcapDevice)
                {
                    // NOTE: AirPcap devices cannot disable local capture
                    var airPcap = device as AirPcapDevice;
                    airPcap.Open(SharpPcap.WinPcap.OpenFlags.DataTransferUdp, readTimeoutMilliseconds);

                }
                else if (device is WinPcapDevice)
                {
                    var winPcap = device as WinPcapDevice;
                    winPcap.Open(SharpPcap.WinPcap.OpenFlags.MaxResponsiveness | SharpPcap.WinPcap.OpenFlags.Promiscuous, readTimeoutMilliseconds);
                }
                else if (device is LibPcapLiveDevice)
                {
                    var livePcapDevice = device as LibPcapLiveDevice;
                    livePcapDevice.Open(DeviceMode.Promiscuous, readTimeoutMilliseconds);

                }
                else
                {
                    //MessageBox.Show("unknown device type of " + device.GetType().ToString());
                    throw new System.InvalidOperationException("unknown device type of " + device.GetType().ToString());
                }

                device.StartCapture();
                //MessageBox.Show("start capturing");

            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.Message);
            }
        }       

        
        /// 关闭监控     
        public void Close()
        {
            //MainTimer.Dispose();
            //Remind.GetRemind().Stop();
            for (int i = 0; i < CaptureDeviceList.Instance.Count; i++)
            {
                var device = CaptureDeviceList.Instance[i];
                device.StopCapture();
            }            
        }       
        /// 处理捕获的包。
        /// 1秒周期的Timer。     
        private void ManageDataTimerEvent(object sender, EventArgs e)
        {           

            // 克隆后清空
            ArrayList _packet_array = new ArrayList();
            _packet_array = (ArrayList)packet_array.Clone();
            packet_array.Clear();

            // 声明变量
            RawCapture ep;
            Packet packet;
            int len;
            TcpPacket tcpPacket;
            UdpPacket udpPacket;           
            // 遍历1秒内捕获的所有包
            for (int i = 0; i < _packet_array.Count; i++)
            {
                // 处理
                ep = ((RawCapture)_packet_array[i]);
                packet = Packet.ParsePacket(ep.LinkLayerType, ep.Data);
                len = ep.Data.Length;

                // 解析成tcp包和udp包
                tcpPacket = (TcpPacket)packet.Extract(typeof(TcpPacket));//PacketDotNet.TcpPacket.GetEncapsulated(packet);
                udpPacket = (UdpPacket)packet.Extract(typeof(UdpPacket));//PacketDotNet.UdpPacket.GetEncapsulated(packet);

                ProtocolType pt = tcpPacket != null ? ProtocolType.TCP : ProtocolType.UDP;

                if (pt == ProtocolType.TCP)//if (tcpPacket != null || udpPacket != null)
                {
                    IpPacket ipPacket = (IpPacket)(tcpPacket.ParentPacket);
                    // 目的、源的IP地址和端口
                    IPAddress srcIp = ipPacket.SourceAddress;
                    IPAddress dstIp = ipPacket.DestinationAddress;
                    int srcPort = tcpPacket.SourcePort;
                    int dstPort = tcpPacket.DestinationPort;                    
                    int port;           // 本机端口
                    bool isUp, isIn;    // 是否上传，是否内网

                    // 判断是否为上传
                    if (srcIp.Equals(hostIP))
                    {
                        port = srcPort;
                        isUp = true;
                        isIn = IsInnerNet(dstIp);
                    }
                    else
                    {
                        port = dstPort;
                        isUp = false;
                        isIn = IsInnerNet(srcIp);
                    }
                    if(isIn == false)
                        lock (thisLock)
                        {
                            TcpIP_array.Add(ep);
                        }
                }
            } 
        }
        public ArrayList GetIntervalTcpIP_packages()
        {
            if (MainTimer.Enabled != true)
                MainTimer.Enabled = true;  //start the timer        
            // 克隆后清空
            lock (thisLock)
            {
                ArrayList _packet_array = new ArrayList(TcpIP_array);
                //_packet_array = (ArrayList)TcpIP_array.Clone();
                TcpIP_array.Clear();
                return _packet_array;
            } 
        }
        public void stop_capture()
        {
            MainTimer.Enabled = false;
            TcpIP_array.Clear();
        }
    }

}
