﻿using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PTZPadController.DataAccessLayer
{
    class SocketAutoConnectParser
    {
        private string m_Host;
        private int m_Port;
        private string m_Name;
        private bool m_Initialized = false;
        private Socket m_Socket;
        private IClientCallback m_ClientCallback;
        private Task<bool> m_Task;
        private bool m_FreeSocket = false;
        private CancellationTokenSource m_Cancellation;


        public bool Connected { get { return (m_Socket != null) && (m_Socket.Connected); } }

        public void Initialize(string name, string host, int port)
        {
            if (!Connected)
            {
                m_Name = name;
                m_Host = host;
                m_Port = port;
                m_Initialized = true;
            }
        }

        public void OpenChanel(IClientCallback callback)
        {
            m_Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                App.logger.Info("{0}, Try connect to {1}:{2}", m_Name, m_Host, m_Port);
                m_Socket.Connect(m_Host, m_Port);
                // Disable the Nagle Algorithm for this tcp socket.
                m_Socket.NoDelay = true;
                //m_Socket.Blocking = false; // This needs to be done after Connect or it will error out.
                m_ClientCallback = callback;

                App.logger.Debug("{0}, {1}, Connected : {2}",m_Name, m_Host, m_Socket.Connected);

            }
            catch (Exception ex)
            {
                App.logger.Error(ex, "{0}, Connection error to {1}",m_Name,m_Host);
            }
            if (m_Socket.Connected)
            {
                App.logger.Debug("Socket for {0}, {1}, Start new Task to recieved data.",m_Name,m_Host);
                m_Task = Task.Factory.StartNew<bool>(ReceiveData, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                m_Task.ContinueWith(SocketConnectionFaulted);
            }
            else
            {
                if (m_Cancellation == null)
                    m_Cancellation = new CancellationTokenSource();
                // Create the task to re-connect.
                var task = Task.Factory.StartNew(() =>
                {
                    if (!m_Cancellation.IsCancellationRequested)
                    {
                        Thread.Sleep(300);
                        App.logger.Info("{0},{1}, try to re-connect ", m_Name, m_Host);
                        OpenChanel(callback);
                    }
                }, m_Cancellation.Token, TaskCreationOptions.None, TaskScheduler.Default);
                if (m_Cancellation.Token.IsCancellationRequested)
                {
                    m_Cancellation = null;
                    m_Socket = null;
                }
            }


        }

        private void ShutDown()
        {
            if ((m_Socket != null) && (m_Socket.Connected))
            {
                // Release the socket.
                m_Socket.Shutdown(SocketShutdown.Both);
                m_Socket.Close();
                m_Socket = null;
            }

            if (m_Cancellation != null)
                m_Cancellation.Cancel();

            App.logger.Debug("{0},{1}, Socket ShutDown",m_Name,m_Host);
        }

        public void CloseChanel()
        {
            m_FreeSocket = true;
            ShutDown();
            m_ClientCallback = null;
            App.logger.Debug("{0},{1}, Socket channel closed", m_Name, m_Host);

        }

        private void SocketConnectionFaulted(Task<bool> task)
        {
            if (!task.Result)
            {
                App.logger.Error("{0},{1}, Socket connection failed, task.Result=fasle", m_Name, m_Host);
                ShutDown();
                OpenChanel(m_ClientCallback);
            }
            else
            {
                App.logger.Info("{0},{1}, Socket connection ended.", m_Name, m_Host);
            }
        }



        private bool ReceiveData()
        {
            var msg = new Byte[4096];
            while (true)
            {
                if (m_FreeSocket)
                    return true;
                if ((m_Socket != null) && (m_Socket.Connected))
                {
                    try
                    {

                        var bytesRead = m_Socket.Receive(msg, 0, 4096, SocketFlags.None);
                        if (bytesRead > 0)
                        {
                            //msgReceived += Encoding.UTF8.GetString(msg, 0, bytesRead);
                            if (App.logger.IsEnabled(LogLevel.Debug))
                                App.logger.Debug("Raw msg : {0}", BitConverter.ToString(msg, 0, bytesRead));
                            if (true)
                                m_ClientCallback.CompletionMessage();
                        }
                        else
                        {
                            App.logger.Info("{0},{1}, The client has disconnected. The socket will be closed.", m_Name, m_Host);
                            break;
                        }
                    }
                    catch (SocketException exception)
                    {
                        if (exception.ErrorCode != 10004)//ErrorCode 10004: A blocking operation was interrupted by a call to WSACancelBlockingCall.
                            App.logger.Error(exception,"{0},{1}, Error 10004 when recieved data", m_Name, m_Host);
                        //else Nothing to do, it's just a socket close.                       
                        return m_FreeSocket;
                    }
                    catch (Exception ex)
                    {
                        App.logger.Error(ex, "{0},{1}, Error when recieved data", m_Name, m_Host);
                        //a socket error has occured
                        return m_FreeSocket;
                    }



                    //App.logger.Debug("Residue after parsing : {0}", msgReceived);

                }
            }
            return false;
        }

        public void SendData(byte[] msg)
        {
            if (App.logger.IsEnabled(LogLevel.Debug))
                App.logger.Debug("{0},{1}, Send data:'{2}'", m_Name, m_Host, BitConverter.ToString(msg));
            if (Connected)
            {
                m_Socket.Send(msg);
                App.logger.Debug("{0},{1}, Data sended.", m_Name, m_Host);
            }
        }

    }
}