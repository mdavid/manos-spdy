using System;
using System.Net.Sockets;

namespace Manos.IO.Managed
{
    internal class UdpSocket : IPSocket<UdpPacket, IStream<UdpPacket>>, IUdpSocket
    {
        private UdpStream stream;

        public UdpSocket(Context context, AddressFamily addressFamily)
            : base(context, addressFamily, ProtocolFamily.Udp)
        {
        }

        #region IUdpSocket Members

        public override void Connect(IPEndPoint endpoint, Action callback, Action<Exception> error)
        {
            if (endpoint == null)
                throw new ArgumentNullException("endpoint");
            if (callback == null)
                throw new ArgumentNullException("callback");
            if (error == null)
                throw new ArgumentNullException("error");

            socket.Connect(endpoint.Address.address, endpoint.Port);
            IsConnected = true;
            callback();
        }

        public override IStream<UdpPacket> GetSocketStream()
        {
            CheckDisposed();

            if (stream == null)
            {
                stream = new UdpStream(this);
            }
            return stream;
        }

        #endregion

        #region Nested type: UdpStream

        private class UdpStream : ManagedStream<UdpPacket>
        {
            private readonly UdpSocket parent;
            private System.Net.EndPoint remote = new System.Net.IPEndPoint(0, 0);

            internal UdpStream(UdpSocket parent)
                : base(parent.Context, 64*1024)
            {
                this.parent = parent;
            }

            public override long Position
            {
                get { throw new NotSupportedException(); }
                set { throw new NotSupportedException(); }
            }

            public override bool CanRead
            {
                get { return true; }
            }

            public override bool CanWrite
            {
                get { return true; }
            }

            protected override long FragmentSize(UdpPacket fragment)
            {
                return 1;
            }

            protected override void DoRead()
            {
                try
                {
                    parent.socket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None,
                                                   ref remote, ReceiveFrom, null);
                }
                catch (System.Net.Sockets.SocketException e)
                {
                    RaiseError(new SocketException("Read failure", Errors.ErrorToSocketError(e.SocketErrorCode)));
                }
            }

            private void ReceiveFrom(IAsyncResult ar)
            {
                if (parent != null)
                {
                    ResetReadTimeout();
                    int length = parent.socket.EndReceiveFrom(ar, ref remote);

                    var ipremote = (System.Net.IPEndPoint) remote;

                    var newBuffer = new byte[length];
                    Buffer.BlockCopy(buffer, 0, newBuffer, 0, length);

                    var info = new UdpPacket(
                        new IPEndPoint(
                            new IPAddress(ipremote.Address.GetAddressBytes()),
                            ipremote.Port),
                        new ByteBuffer(newBuffer));

                    Context.Enqueue(delegate
                                        {
                                            RaiseData(info);
                                            DispatchRead();
                                        });
                }
            }

            protected override WriteResult WriteSingleFragment(UdpPacket packet)
            {
                var ep = new System.Net.IPEndPoint(
                    new System.Net.IPAddress(packet.IPEndPoint.Address.GetAddressBytes()), packet.IPEndPoint.Port);
                parent.socket.BeginSendTo(packet.Buffer.Bytes, packet.Buffer.Position, packet.Buffer.Length,
                                          SocketFlags.None, ep, WriteCallback, null);

                return WriteResult.Consume;
            }

            private void WriteCallback(IAsyncResult ar)
            {
                Context.Enqueue(delegate
                                    {
                                        if (parent != null)
                                        {
                                            ResetWriteTimeout();

                                            System.Net.Sockets.SocketError err;
                                            parent.socket.EndSend(ar, out err);
                                            if (err == System.Net.Sockets.SocketError.Success)
                                            {
                                                HandleWrite();
                                            }
                                            else
                                            {
                                                RaiseError(new SocketException("Write failure",
                                                                               Errors.ErrorToSocketError(err)));
                                            }
                                        }
                                    });
            }
        }

        #endregion
    }
}