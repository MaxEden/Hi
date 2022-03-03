# Hi!
Hi is a simple tcp/udp communication library for inter-process communication and local network communication.   
It consists out of a udp discovery server/client and a tcp server/client.   
It's deliberately ascetic and supports only simple strings as messages, byte array as a payload and mostly targeted to one-to-one communication.   
It's made for simple inter-process communication, mobile-to-desktop applications and IoT solutions.   
Text messages format implies trivial and streamlined debugging and is easily extensible with something like JSON.   

```c#
_hiserver = new HiServer
{
    Receive = Receive,
    ManualMessagePolling = true,
    Log = HiLog
};
_hiserver.Open("MyService");

var str = JsonConvert.SerializeObject(msg, new JsonSerializerSettings()
            {
                Formatting = Formatting.Indented,
                TypeNameHandling = TypeNameHandling.All
            });

_hiserver.Send(str);

private Msg Receive(Msg msg, Sender sender)
{
    object command = null;
    try
    {
        command = JsonConvert.DeserializeObject(msg.Text, new JsonSerializerSettings()
        {
            TypeNameHandling = TypeNameHandling.All
        });
		return "Received:" + msg.Text;
    }
    catch
    {
		return null;
    }
}

private void Loop(){
	_hiserver.PollMessages();
}
```


```c#
_client = new HiClient(_name);
_client.Log = log;
_client.Receive = receive;
_client.ManualMessagePolling = true;

_client.Connect("MyService");
_client.SendBlocking("Hi there!");

public void Update()
{
    if(_client.IsConnected)
    {
        _client.PollMessages();
    }
}
```
