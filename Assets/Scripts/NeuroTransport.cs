using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
namespace ForceDemo {
// TCP is confined to loopback. Worker threads never touch Unity objects.
public sealed class NeuroTransport:IDisposable {
    public readonly ConcurrentQueue<string> inbox=new ConcurrentQueue<string>();
    public volatile bool connected;public string error="";volatile bool stopping;TcpListener listener;TcpClient client;Thread thread;readonly object sync=new object();
    const int MaxLine=65536,MaxQueue=256;
    public void Start(int port=5555){
        try{listener=new TcpListener(IPAddress.Loopback,port);listener.Start(1);thread=new Thread(Receive){IsBackground=true,Name="Neuro input receiver"};thread.Start();}
        catch(Exception e){error=e.Message;listener?.Stop();}
    }
    void Enqueue(string value){if(inbox.Count>=MaxQueue){while(inbox.TryDequeue(out _)){};inbox.Enqueue("{\"event\":\"bridge_overflow\"}");return;}inbox.Enqueue(value);}
    void Receive(){
        while(!stopping){
            try{
                var accepted=listener.AcceptTcpClient();accepted.NoDelay=true;lock(sync)client=accepted;connected=true;Enqueue("{\"event\":\"bridge_connected\"}");
                using(accepted)using(var reader=new StreamReader(accepted.GetStream(),new UTF8Encoding(false,true),false,4096)){
                    var line=new StringBuilder();int next;
                    while(!stopping&&(next=reader.Read())>=0){if(next=='\n'){if(line.Length>0)Enqueue(line.ToString());line.Clear();}else if(next!='\r'){line.Append((char)next);if(line.Length>MaxLine)throw new IOException("Neuro message exceeds size limit.");}}
                }
            }catch(Exception e){if(!stopping)error=e is SocketException?"Waiting for input service.":e.Message;}
            finally{connected=false;lock(sync){client?.Dispose();client=null;}if(!stopping)Enqueue("{\"event\":\"bridge_disconnected\"}");}
        }
    }
    public bool Send(string line){lock(sync){if(!connected||client==null)return false;try{byte[] data=Encoding.UTF8.GetBytes(line+"\n");client.GetStream().Write(data,0,data.Length);return true;}catch(IOException){connected=false;return false;}catch(ObjectDisposedException){connected=false;return false;}}}
    public void Dispose(){stopping=true;connected=false;listener?.Stop();lock(sync){client?.Dispose();client=null;}thread?.Join(500);}
}
}
