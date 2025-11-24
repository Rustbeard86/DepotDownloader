using System;
using System.Diagnostics.Tracing;
using System.Text;

namespace DepotDownloader;

internal sealed class HttpDiagnosticEventListener : EventListener
{
    private const EventKeywords TasksFlowActivityIds = (EventKeywords)0x80;

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        switch (eventSource.Name)
        {
            case "System.Net.Http" or "System.Net.Sockets" or "System.Net.Security" or "System.Net.NameResolution":
                EnableEvents(eventSource, EventLevel.LogAlways);
                break;
            case "System.Threading.Tasks.TplEventSource":
                EnableEvents(eventSource, EventLevel.LogAlways, TasksFlowActivityIds);
                break;
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        var sb = new StringBuilder().Append(
            $"{eventData.TimeStamp:HH:mm:ss.fffffff}  {eventData.EventSource.Name}.{eventData.EventName}(");
        for (var i = 0; i < eventData.Payload?.Count; i++)
        {
            sb.Append(eventData.PayloadNames?[i]).Append(": ").Append(eventData.Payload[i]);
            if (i < eventData.Payload?.Count - 1) sb.Append(", ");
        }

        sb.Append(')');
        Console.WriteLine(sb.ToString());
    }
}