using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Threading;
using LookingGlass.Toolkit.Bridge;

namespace LookingGlass.Toolkit {
    public class DefaultHttpSender : IHttpSender, IDisposable {
        private class AsyncMessageRecord {
            public string url;
            public string content;
            public Action<string> onCompletion;

            public AsyncMessageRecord(string url, string content, Action<string> onCompletion) {
                this.url = url;
                this.content = content;
                this.onCompletion = onCompletion;
            }
        }

        private HttpClient innerClient;

        private volatile bool stopAsyncThread = false;
        private Thread asyncSendThread;
        private ConcurrentQueue<AsyncMessageRecord> toSend;

        public int TimeoutSeconds {
            get { return (int) innerClient.Timeout.TotalSeconds; }
            set { innerClient.Timeout = TimeSpan.FromSeconds(value); }
        }

        public Action<Exception> ExceptionHandler { get; set; }

        public DefaultHttpSender() {
            innerClient = new HttpClient();
            EndAsync();
            BeginAsync();
        }

        public void Dispose() {
            EndAsync();
            if (innerClient != null) {
                innerClient.Dispose();
                innerClient = null;
            }
        }

        private HttpMethod GetInnerMethod(HttpSenderMethod method) {
            switch (method) {
                case HttpSenderMethod.Get:      return HttpMethod.Get;
                case HttpSenderMethod.Post:     return HttpMethod.Post;
                case HttpSenderMethod.Put:      return HttpMethod.Put;
                default:
                    throw new NotSupportedException("Unsupported HTTP method: " + method);
            }
        }

        public string Send(HttpSenderMethod method, string url, string content) {
            HttpRequestMessage request = new HttpRequestMessage(GetInnerMethod(method), content);
            request.Content = new StringContent(content);
            HttpResponseMessage response = innerClient.SendAsync(request).Result;
            return response.Content.ReadAsStringAsync().Result;
        }

        public void SendAsync(HttpSenderMethod method, string url, string content, Action<string> onCompletion) {
            if (toSend != null) {
                toSend.Enqueue(new AsyncMessageRecord(url, content, onCompletion));
            } else {
                try {
                    HttpRequestMessage request = new HttpRequestMessage(GetInnerMethod(method), content);
                    request.Content = new StringContent(content);
                    HttpResponseMessage response = innerClient.SendAsync(request).Result;
                    string result = response.Content.ReadAsStringAsync().Result;
                    onCompletion(result);
                } catch (Exception e) {
                    onCompletion("");
                    if (ExceptionHandler != null)
                        ExceptionHandler(e);
                    else throw;
                }
            }
        }

        public void BeginAsync() {
            toSend = new ConcurrentQueue<AsyncMessageRecord>();
            stopAsyncThread = false;

            asyncSendThread = new Thread(() => {
                while (!stopAsyncThread) {
                    if (toSend.TryDequeue(out AsyncMessageRecord message)) {
                        try {
                            string result = Send(HttpSenderMethod.Put, message.url, message.content);
                            message.onCompletion(result);
                        } catch (Exception e) {
                            message.onCompletion("");
                            if (ExceptionHandler != null)
                                ExceptionHandler(e);
                            else throw;
                        }
                    }
                    Thread.Sleep(1);
                }
            });

            //NOTE: This ensures the thread closes no matter what, even if we crash
            asyncSendThread.IsBackground = true;
            asyncSendThread.Start();
        }

        public void EndAsync() {
            stopAsyncThread = true;
            asyncSendThread?.Join();
        }
    }
}
