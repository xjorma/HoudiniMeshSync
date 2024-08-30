using System;

namespace LookingGlass.Toolkit {
    public interface IHttpSender {
        public int TimeoutSeconds { get; set; }
        Action<Exception> ExceptionHandler { get; set; } //REVIEW: Remove this when using DI, and use ILogger instead? But we'll need BridgeConnectionHTTP class to know about failures, to UpdateConnectionState(false)..
        public string Send(HttpSenderMethod method, string url, string content);
        public void SendAsync(HttpSenderMethod method, string url, string content, Action<string> onCompletion);
    }
}
