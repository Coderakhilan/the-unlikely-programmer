using System;
using System.Collections.Generic;

namespace Unlikely.Core
{
    /// <summary>What's on Donnie's phone: message threads (some with a downloadable app attached).</summary>
    public static class PhoneData
    {
        public sealed class Message
        {
            public string text;
            public bool fromMe;
            public string attachment; // e.g. "manual": downloading it installs the Manual app
        }

        public sealed class Thread
        {
            public string contact;
            public readonly List<Message> messages = new List<Message>();
            public bool unread;
        }

        public static readonly List<Thread> Threads = new List<Thread>();
        public static event Action<Thread> MessageArrived;

        public static bool AnyUnread => Threads.Exists(t => t.unread);

        public static Thread AddMessage(string contact, string text, string attachment = null, bool fromMe = false)
        {
            var t = Threads.Find(x => x.contact == contact);
            if (t == null) { t = new Thread { contact = contact }; Threads.Insert(0, t); }
            else { Threads.Remove(t); Threads.Insert(0, t); }
            t.messages.Add(new Message { text = text, fromMe = fromMe, attachment = attachment });
            if (!fromMe) t.unread = true;
            MessageArrived?.Invoke(t);
            return t;
        }

        public static void Clear() => Threads.Clear();

        public static void ResetStatic()
        {
            Threads.Clear();
            MessageArrived = null;
        }
    }
}
