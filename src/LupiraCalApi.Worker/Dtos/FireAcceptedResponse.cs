namespace LupiraCalApi.Worker.Dtos;

/// <summary>assistant-api's 202 body. <c>Duplicate</c> = the dedupe key was already recorded (a lost-ack re-push).</summary>
public sealed class FireAcceptedResponse
{
    public required Guid InboundItemId { get; set; }
    public required bool Duplicate { get; set; }
}
