using System;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.Pipeline;

public class RandomMessageTypeIncoming :
    Behavior<IIncomingPhysicalMessageContext>
{
    static readonly string[] types = { "A", "B", "C" };

    private static readonly string[] a = { "Order", "Customer", "Email", "Employee", "Audit", "Payment", "Report", "Booking" };
    private static readonly string[] b = { "Accepted", "Approved", "Canceled", "Closed", "Send", "Reviewed", "Promoted", "" };
    public override Task Invoke(IIncomingPhysicalMessageContext context, Func<Task> next)
    {
        var messageType = RandomNext(a) + RandomNext(b);
        var enclosedMessageTypes = $"Messages.{messageType}, Demo, Version = 1.0.0.0, Culture = neutral, PublicKeyToken = null";
        context.Message.Headers[Headers.EnclosedMessageTypes] = enclosedMessageTypes;
        return next();
    }

    static T RandomNext<T>(T[] types)
    {
        return types[ThreadLocalRandom.Next(types.Length)];
    }
}