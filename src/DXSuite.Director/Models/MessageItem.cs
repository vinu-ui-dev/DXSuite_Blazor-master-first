using System.Text.Json;
using Newtonsoft.Json;
using DXSuite.Director.Entities;

namespace DXSuite.Director.Models
{

    public class MessageAssignment
    {
        public List<MessageItem> Messages { get; set; } = new List<MessageItem>();


        public MessageAssignment()
        {

        }

        public MessageAssignment(List<MessageItem> messages)
        {
            Messages = messages;
        }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    public class MessageItem
    {
        public string Message { get; set; } = string.Empty;
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }

        public MessageItem()
        {
            StartDateTime = DateTime.Now;
            EndDateTime = DateTime.Now.AddHours(4);
        }

        public MessageItem(string message, DateTime startDateTime, DateTime endDateTime)
        {
            Message = message;
            StartDateTime = startDateTime;
            EndDateTime = endDateTime;
        }
    }
}
