using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using dataflow_monitor.core;
using System.Collections;
using PacketDotNet;
using System.Net;
using System.Net.Sockets;
using SharpPcap;

namespace dataflow_monitor
{
    public partial class Form1 : Form
    {
        FlowMonitor fm;                               
        Timer MainTimer = new Timer();  // 每秒对抓包数据进行分析的 Timer
        System.Timers.Timer MainTimer2 = new System.Timers.Timer();
        IPAddress hostIP;                                          // 本机IP地址  
        double total_up =0.0;
        double total_down =0.0;
        double TotalFlow = 0.0;
        double threshold = 1;
        bool first_time = false;
        private Object thisLock = new Object();//多线程加锁
        public Form1()
        {
            InitializeComponent();
            
            hostIP = getHostIPAddress();
            //每隔一段时间检查是否流量超出
            MainTimer2.Interval = 1000*60*5;
            MainTimer2.Elapsed += Over_Traffic_Flow;
            // 每秒处理数据            
            MainTimer.Interval = 1000;
            MainTimer.Tick += update;
            //处理listview的表头
            ColumnHeader h1 = new ColumnHeader();
            h1.Text = "Protocol";
            //h1.Width = 150;
            ColumnHeader h2 = new ColumnHeader();
            h2.Text = "Source IP";
            h2.Width = 120;
            ColumnHeader h3 = new ColumnHeader();
            h3.Text = "Destination IP";
            h3.Width = 120;
            ColumnHeader h4 = new ColumnHeader();
            h4.Text = "Length（Byte）";
            h4.Width = 100;
            listView1.Columns.AddRange(new ColumnHeader[]{h1,h2,h3,h4});
            listView1.View = View.Details;           
            
        }
        //public delegate void OutDelegate(string text);
        public delegate void OutDelegate(ArrayList TcpIP_RawPackages);
        public delegate void ExcessFlow(double threshold );
        
        public void ShowPackets(ArrayList TcpIP_RawPackages)
        {
            //Flow flow = new Flow(); //处理流量的类
            RawCapture ep;
            Packet packet;
            double len;
            TcpPacket tcpPacket;
#region Declare Variable 声明变量
            for (int i = 0; i < TcpIP_RawPackages.Count; i++)
            {
                // 处理
                ep = ((RawCapture)TcpIP_RawPackages[i]);
                packet = Packet.ParsePacket(ep.LinkLayerType, ep.Data);
                len = ep.Data.Length;
                tcpPacket = (TcpPacket)packet.Extract(typeof(TcpPacket));
                if (tcpPacket != null)//if (tcpPacket != null || udpPacket != null)
                {
                    IpPacket ipPacket = (IpPacket)(tcpPacket.ParentPacket);
                    // 目的、源的IP地址和端口
                    IPAddress srcIp = ipPacket.SourceAddress;
                    IPAddress dstIp = ipPacket.DestinationAddress;

                    int srcPort = tcpPacket.SourcePort;
                    int dstPort = tcpPacket.DestinationPort;
                    ListViewItem lvItem1 = new ListViewItem();
                    lvItem1.SubItems.Clear();
                    lvItem1.SubItems[0].Text="TCP";
                    lvItem1.SubItems.Add(srcIp.ToString());
                    lvItem1.SubItems.Add(dstIp.ToString());
                    lvItem1.SubItems.Add(len.ToString("#.###"));
                    listView1.Items.Add(lvItem1);
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
                    
                    if (!isIn && isUp) total_up += (len/1024/1024);
                    if (!isIn && !isUp) total_down += (len/1024/1024);
                    if(!isIn) TotalFlow += (len /1024);
                }

            }
#endregion
            if (TotalFlow > 1024)
            {
                total_flow_lable.Text = (TotalFlow / 1024).ToString("#.###");
                flow_unit.Text = "MB";                
            }
            else
                total_flow_lable.Text = TotalFlow.ToString("#.###");

            if (TotalFlow > threshold * 1024)
            {
                ExcessFlow excessFlow = new ExcessFlow(data_flow_warn);//ShowPackets OutText
                this.BeginInvoke(excessFlow, new object[] { threshold });
            }
            
                       
        }
        
        public void Over_Traffic_Flow(object sender, EventArgs e)
        {
            if (TotalFlow > threshold * 1024)
                MessageBox.Show("data flow has beyond " + threshold.ToString() + " MB");
        }
        private void button1_Click(object sender, EventArgs e)
        {
            if(fm == null)
                fm = FlowMonitor.GetMonitor();
            MainTimer.Enabled = true;  //start the timer            
             
        }

        private void button2_Click(object sender, EventArgs e)
        {            
            MainTimer.Enabled = false;
            fm.stop_capture();
        }       
        private void update(object sender, EventArgs e)
        {
            //Flow flow = new Flow(); //处理流量的类
            ArrayList TcpIP_RawPackages = fm.GetIntervalTcpIP_packages();

            OutDelegate outdelegate = new OutDelegate(ShowPackets);//ShowPackets OutText
            this.BeginInvoke(outdelegate, new object[] { TcpIP_RawPackages });
            //flow_label.Text = System.Convert.ToString(flow.lastUp+flow.lastDown);
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

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            System.Environment.Exit(0);
        }

        private void textBox1_Enter(object sender, EventArgs e)
        {
            this.textBox1.Text = "";
        }   

        private void textBox1_Leave(object sender, EventArgs e)
        {
            if (textBox1.Text == "")
                textBox1.Text = "[Default:50]";
            else
                threshold = Double.Parse(textBox1.Text);
        }

        private void button3_Click(object sender, EventArgs e)
        {
            if (textBox1.Text == "")
                textBox1.Text = "[Default:50]";
            else
                threshold = Double.Parse(textBox1.Text);
        }

        private void button4_Click(object sender, EventArgs e)
        {
            //stop capture packets
            MainTimer.Enabled = false;
            fm.stop_capture();
            //clear the data on the form
            listView1.Items.Clear();
            total_flow_lable.Text = "0.0";
            TotalFlow = 0;
        }

        private void data_flow_warn(double threshold)
        {
            if (first_time == false)
                MessageBox.Show("data flow has beyond " + threshold.ToString() + " MB");
            first_time = true;
        }        

             
             
    }
}
