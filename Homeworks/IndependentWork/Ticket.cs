namespace IndependentWork;

public class Ticket
{
    public int Id { get; set; }
    public string Subject { get; set; } = "";
    public string Category { get; set; } = "";
    public string Status { get; set; } = "open";
}