﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;
using System.Net.Security;
using System.Security.Authentication;

namespace NetworkSocket
{
    /// <summary>
    /// 表示Tcp客户端抽象类
    /// </summary>   
    public abstract class TcpClientBase : IWrapper, IDisposable
    {
        /// <summary>
        /// 会话对象
        /// </summary>
        private TcpSessionBase session;

        /// <summary>
        /// 获取远程终结点
        /// </summary>
        public EndPoint RemoteEndPoint
        {
            get
            {
                return this.session.RemoteEndPoint;
            }
        }

        /// <summary>
        /// 获取是否已连接到远程端
        /// </summary>
        public bool IsConnected
        {
            get
            {
                return this.session.IsConnected;
            }
        }

        /// <summary>
        /// 获取用户附加数据
        /// </summary>
        public ITag Tag
        {
            get
            {
                return this.session.Tag;
            }
        }

        /// <summary>
        /// 获取或设置断线自动重连的时间间隔 
        /// 设置为TimeSpan.Zero表示不自动重连
        /// </summary>
        public TimeSpan ReconnectPeriod { get; set; }

        /// <summary>
        /// 获取或设置心跳包时间间隔
        /// 设置为TimeSpan.Zero表示不发心跳包
        /// </summary>
        public TimeSpan KeepAlivePeriod { get; set; }

        /// <summary>
        /// Tcp客户端抽象类
        /// </summary>
        public TcpClientBase()
        {
            this.session = new IocpTcpSession();
            this.BindHandler(this.session);
        }

        /// <summary>
        /// SSL支持的Tcp客户端抽象类
        /// </summary>
        /// <param name="targetHost">目标主机</param>
        /// <exception cref="ArgumentNullException"></exception>
        public TcpClientBase(string targetHost)
            : this(targetHost, null)
        {
        }

        /// <summary>
        /// SSL支持的Tcp客户端抽象类
        /// </summary>  
        /// <param name="targetHost">目标主机</param>
        /// <param name="certificateValidationCallback">远程证书验证回调</param>
        /// <exception cref="ArgumentNullException"></exception>
        public TcpClientBase(string targetHost, RemoteCertificateValidationCallback certificateValidationCallback)
        {
            this.session = new SslTcpSession(targetHost, certificateValidationCallback);
            this.BindHandler(this.session);
        }

        /// <summary>
        /// 绑定会话的处理方法
        /// </summary>
        /// <param name="session">会话</param>
        private void BindHandler(TcpSessionBase session)
        {
            session.ReceiveHandler = this.ReceiveHandler;
            session.DisconnectHandler = this.DisconnectHandler;
        }

        /// <summary>
        /// 连接到远程端
        /// </summary>
        /// <param name="host">域名或ip地址</param>
        /// <param name="port">远程端口</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="SocketException"></exception>
        /// <returns></returns>
        public Task<SocketError> ConnectAsync(string host, int port)
        {
            return this.ConnectAsync(new DnsEndPoint(host, port));
        }

        /// <summary>
        /// 连接到远程终端       
        /// </summary>
        /// <param name="ip">远程ip</param>
        /// <param name="port">远程端口</param>
        /// <returns></returns>
        public Task<SocketError> ConnectAsync(IPAddress ip, int port)
        {
            return this.ConnectAsync(new IPEndPoint(ip, port));
        }

        /// <summary>
        /// 连接到远程终端 
        /// </summary>
        /// <param name="remoteEndPoint">远程ip和端口</param> 
        /// <exception cref="AuthenticationException"></exception>
        /// <returns></returns>
        public Task<SocketError> ConnectAsync(EndPoint remoteEndPoint)
        {
            if (remoteEndPoint == null)
            {
                throw new ArgumentNullException();
            }

            if (this.IsConnected == true)
            {
                return Task.FromResult(SocketError.Success);
            }


            var addressFamily = AddressFamily.InterNetwork;
            if (remoteEndPoint.AddressFamily != AddressFamily.Unspecified)
            {
                addressFamily = remoteEndPoint.AddressFamily;
            }

            var taskSource = new TaskCompletionSource<SocketError>();
            var socket = new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp);
            var connectArg = new SocketAsyncEventArgs { RemoteEndPoint = remoteEndPoint, UserToken = taskSource };
            connectArg.Completed += this.ConnectCompleted;

            if (socket.ConnectAsync(connectArg) == false)
            {
                this.ConnectCompleted(socket, connectArg);
            }
            return taskSource.Task;
        }


        /// <summary>
        /// 连接完成事件
        /// </summary>
        /// <param name="sender">连接者</param>
        /// <param name="e">事件参数</param>
        private void ConnectCompleted(object sender, SocketAsyncEventArgs e)
        {
            var socket = sender as Socket;
            var taskSource = e.UserToken as TaskCompletionSource<SocketError>;

            if (e.SocketError == SocketError.Success)
            {
                this.session.Bind(socket);
                this.session.TrySetKeepAlive(this.KeepAlivePeriod);
                this.session.LoopReceive();
            }
            else
            {
                socket.Dispose();
            }

            e.Completed -= this.ConnectCompleted;
            e.Dispose();

            taskSource.TrySetResult(e.SocketError);
            this.OnConnected(e.SocketError);
        }


        /// <summary>
        /// 连接到远程端
        /// </summary>
        /// <param name="host">域名或ip地址</param>
        /// <param name="port">远程端口</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <returns></returns>
        public SocketError Connect(string host, int port)
        {
            return this.Connect(new DnsEndPoint(host, port));
        }

        /// <summary>
        /// 连接到远程终端       
        /// </summary>
        /// <param name="ip">远程ip</param>
        /// <param name="port">远程端口</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <returns></returns>
        public SocketError Connect(IPAddress ip, int port)
        {
            return this.Connect(new IPEndPoint(ip, port));
        }

        /// <summary>
        /// 连接到远程端
        /// </summary>
        /// <param name="remoteEndPoint">远程端</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <returns></returns>
        public SocketError Connect(EndPoint remoteEndPoint)
        {
            if (remoteEndPoint == null)
            {
                throw new ArgumentNullException();
            }

            var addressFamily = AddressFamily.InterNetwork;
            if (remoteEndPoint.AddressFamily != AddressFamily.Unspecified)
            {
                addressFamily = remoteEndPoint.AddressFamily;
            }

            try
            {
                var socket = new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp);
                socket.Connect(remoteEndPoint);

                this.session.Bind(socket);
                this.session.TrySetKeepAlive(this.KeepAlivePeriod);
                this.session.LoopReceive();

                this.OnConnected(SocketError.Success);
                return SocketError.Success;
            }
            catch (SocketException ex)
            {
                this.OnConnected(ex.SocketErrorCode);
                return ex.SocketErrorCode;
            }
        }

        /// <summary>
        /// 接收处理
        /// </summary>
        /// <param name="session">会话</param>
        private void ReceiveHandler(TcpSessionBase session)
        {
            this.OnReceive(session.InputStream);
        }

        /// <summary>
        /// 关闭连接处理
        /// </summary>
        /// <param name="session">会话</param>
        private void DisconnectHandler(TcpSessionBase session)
        {
            session.Close(false);
            this.OnDisconnected();
            this.ReconnectLoopAsync();
        }


        /// <summary>
        /// 与服务器连接之后，将触发此方法
        /// </summary>
        /// <param name="error">连接状态码</param>
        protected virtual void OnConnected(SocketError error)
        {
        }

        /// <summary>
        /// 当与服务器断开连接后，将触发此方法
        /// </summary>       
        protected virtual void OnDisconnected()
        {
        }

        /// <summary>
        /// 当接收到远程端的数据时，将触发此方法   
        /// </summary>       
        /// <param name="inputStream">接收到的数据</param>
        /// <returns></returns>
        protected abstract void OnReceive(IStreamReader inputStream);


        /// <summary>
        /// 同步发送数据
        /// </summary>
        /// <param name="buffer">数据</param>  
        /// <exception cref="ArgumentNullException"></exception>        
        /// <exception cref="SocketException"></exception>
        /// <returns></returns>
        public virtual int Send(byte[] buffer)
        {
            return this.session.Send(buffer);
        }

        /// <summary>
        /// 异步发送数据
        /// </summary>
        /// <param name="byteRange">数据范围</param>  
        /// <exception cref="ArgumentNullException"></exception>        
        /// <exception cref="SocketException"></exception>
        /// <returns></returns>
        public virtual int Send(ArraySegment<byte> byteRange)
        {
            return this.session.Send(byteRange);
        }

        /// <summary>     
        /// 等待缓冲区数据发送完成
        /// 然后断开和远程端的连接   
        /// </summary>     
        public virtual void Close()
        {
            this.session.Close(true);
        }

        /// <summary>
        /// 还原到包装前
        /// </summary>
        /// <returns></returns>
        public ISession UnWrap()
        {
            return this.session;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public virtual void Dispose()
        {
            this.session.Dispose();
        }

        /// <summary>
        /// 循环尝试间隔地重连
        /// </summary>
        private async void ReconnectLoopAsync()
        {
            if (this.ReconnectPeriod <= TimeSpan.Zero)
            {
                return;
            }

            var state = await this.ConnectAsync(this.RemoteEndPoint);
            if (state == SocketError.Success)
            {
                return;
            }

            await Task.Delay(this.ReconnectPeriod);
            this.ReconnectLoopAsync();
        }
    }
}