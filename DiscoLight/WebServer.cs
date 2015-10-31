using System;
using System.Collections.Generic;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace DiscoLight
{
    // Method to be called when URL rule is met
    public delegate Task<WebResponse> RuleDeletage(WebRequest response);

    // Delegate type for the error event
    public delegate void ErrorOccured(int code, string message);

    public struct WebRequest
    {
        public Dictionary<string, string> Header;
        public string Method;
        public string Uri;
        public Stream Content;
    }

    public struct WebResponse
    {
        public Dictionary<string, string> Header;
        public string Method;
        public string Uri;
        public Stream Content;
    }

    public class WebServer
    {
        /// <summary>
        ///     Isolated storage instance as server needs to save temp files
        /// </summary>
        private readonly IsolatedStorageFile _isf = IsolatedStorageFile.GetUserStoreForApplication();

        /// <summary>
        ///     Socket listener - the main IO part
        /// </summary>
        private readonly StreamSocketListener _listener = new StreamSocketListener();

        /// <summary>
        ///     Rules: URL Reg-ex => method to be called when rule is met
        /// </summary>
        private readonly Dictionary<Regex, RuleDeletage> _serverRules;

        /// <summary>
        ///     Indicates if server should be listening
        /// </summary>
        protected bool IsListening;

        /// <summary>
        ///     Random number generator
        /// </summary>
        protected Random Rnd = new Random();

        /// <summary>
        ///     Initializes the new server object and puts it in listening mode
        /// </summary>
        /// <param name="rules">Set of rules in a form "URL Reg-ex" => "Method to be fired when rule is met"</param>
        /// <param name="ip">IP to bind to</param>
        /// <param name="port">Port to bind to</param>
        public WebServer(Dictionary<Regex, RuleDeletage> rules, string ip, string port)
        {
            // Assign passed rules to the server
            _serverRules = rules;
            try
            {
                // Try to turn on the server
                Task.Run(async () =>
                {
                    // Start listening
                    _listener.ConnectionReceived += listener_ConnectionReceived;

                    // Bind to IP:Port
                    await _listener.BindEndpointAsync(new HostName(ip), port);

                    IsListening = true;
                });
            }
            catch (Exception ex)
            {
                // If possible fire the error event with the exception message
                ErrorOccured?.Invoke(-1, ex.Message);
            }
        }

        /// <summary>
        ///     Event fired when an error occurred
        /// </summary>
        public event ErrorOccured ErrorOccured;

        private void listener_ConnectionReceived(StreamSocketListener sender,
            StreamSocketListenerConnectionReceivedEventArgs args)
        {
            // If we should not be listening anymore, yet for some reason request was still parsed (phone not yet 
            // closed the socket) exit the method as it may be unwanted by the user for anybody to read any data
            if (IsListening == false)
            {
                return;
            }

            // Write request to a temporary file (better than memory in case of future handling of big post requests
            var filename = "/temp_" + Rnd.Next(100, 999) + Rnd.Next(1000, 9999);

            // Get the request socket
            var sck = args.Socket;

            // Create a new file based on the temp file name
            var plik = _isf.CreateFile(filename);

            // Create a new stream reader object
            var read = new DataReader(sck.InputStream) {InputStreamOptions = InputStreamOptions.Partial};

            // Defines if we should stop reading
            var finished = false;

            // Small buffer will make requests long, too big may cut connections (probably something missing in the 
            // web server)
            const uint maxbuf = 4096;

            // Create new task
            Task.Run(async () =>
            {
                // Yeah I know it is bad, yet does it's job :)
                while (true)
                {
                    // Wait for full buffer
                    await read.LoadAsync(maxbuf);

                    // If there is any data in the buffer
                    if (read.UnconsumedBufferLength > 0)
                    {
                        // Create a new byte buffer (for our internal use) long as the data in the socket buffer
                        var len = read.UnconsumedBufferLength;
                        var buffer = new byte[read.UnconsumedBufferLength];

                        // Read the data from the socket to the internal buffer
                        read.ReadBytes(buffer);

                        // Write the new data to the temporary file
                        plik.Write(buffer, 0, buffer.Length);

                        // If buffer was not filled it means that we got the last packet
                        if (len < maxbuf)
                        {
                            finished = true;
                        }
                    }
                    else
                    {
                        // No data in the buffer: finished
                        finished = true;
                    }

                    // If we finished...
                    if (finished != true) continue;

                    // Close the temporary file
                    plik.Dispose();

                    // Pass it for further parsing
                    ParseRequest(filename, sck);

                    // Break the loop
                    break;
                }
            });
        }

        /// <summary>
        ///     Method which parses the request and decides about the action
        /// </summary>
        /// <param name="requestFile">Path to the temporary file holding the request packet</param>
        /// <param name="socket">Socket used with the request (required for response)</param>
        public async void ParseRequest(string requestFile, StreamSocket socket)
        {
            // Open and read the request: needs stream reading support for large files
            var plika = _isf.OpenFile(requestFile, FileMode.Open);
            var reada = new StreamReader(plika);

            // Read the first ling to get request type
            var linge = reada.ReadLine();

            // Create new request object (same type as response object - possibly better class naming needs to be used)
            var request = new WebRequest();

            // If there is any data...
            if (!string.IsNullOrEmpty(linge))
            {
                // Get the method (currently GET only supported really) and the request URL
                if (linge.Substring(0, 3) == "GET")
                {
                    request.Method = "GET";
                    request.Uri = linge.Substring(4);
                }
                else if (linge.Substring(0, 4) == "POST")
                {
                    request.Method = "POST";
                    request.Uri = linge.Substring(5);
                }

                // Remove the HTTP version
                request.Uri = Regex.Replace(request.Uri, " HTTP.*$", string.Empty);

                // Create a dictionary for the sent headers
                var headers = new Dictionary<string, string>();

                // Read HTTP headers into the dictionary
                string line;
                do
                {
                    line = reada.ReadLine();
                    var sepa = new string[1];
                    sepa[0] = ":";
                    var elems = line.Split(sepa, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (elems.Length > 0)
                    {
                        headers.Add(elems[0], elems[1]);
                    }
                } while (line.Length > 0);

                // Assign headers to the request object
                request.Header = headers;

                // Assign rest of the content to the request object in a form of a stream handle moved to the part with content after previous read line operations
                request.Content = reada.BaseStream;

                // Determines if we found a matching URL rule
                var foundrule = false;

                // Create a stream writer to the output stream (response)
                var writ = new DataWriter(socket.OutputStream);

                // If there are any server rules
                if (_serverRules != null)
                {
                    // For every rule...
                    foreach (
                        var toSendTask in
                            from rulePart in _serverRules.Keys
                            where rulePart.IsMatch(request.Uri)
                            select _serverRules[rulePart](request))
                    {
                        // Mark that we found a rule
                        foundrule = true;

                        // Wait for the response to get fulfilled
                        var toSend = await toSendTask;

                        // If the rule is meant to redirect...
                        writ.WriteString(toSend.Header.ContainsKey("Location")
                            ? "HTTP/1.1 302\r\n"
                            : "HTTP/1.1 200 OK\r\n");

                        // Write content length to the buffer
                        writ.WriteString("Content-Length: " + (toSend.Content == null ? 0 : toSend.Content.Length) +
                                         "\r\n");

                        // For each of the response headers (returned by the delegate assigned to the URL rule
                        foreach (var header in toSend.Header)
                        {
                            // Write it to the output
                            writ.WriteString(header.Key + ": " + header.Value + "\r\n");
                        }

                        // Add connection: close header
                        writ.WriteString("Connection: close\r\n");

                        // New line before writing content
                        writ.WriteString("\r\n");

                        // We lock the web server until response is finished
                        lock (this)
                        {
                            // Reset the output stream
                            if (toSend.Content != null)
                            {
                                toSend.Content.Seek(0, SeekOrigin.Begin);
                                Task.Run(async () =>
                                {
                                    await writ.StoreAsync(); // Wait for the data to be saved in the output
                                    await writ.FlushAsync(); // Flush (send to the output)

                                    // Write the data to the output using 1024 buffer (store and flush after every loop)
                                    while (toSend.Content.Position < toSend.Content.Length)
                                    {
                                        var buffer = toSend.Content.Length - toSend.Content.Position < 1024
                                            ? new byte[toSend.Content.Length - toSend.Content.Position]
                                            : new byte[1024];
                                        toSend.Content.Read(buffer, 0, buffer.Length);
                                        writ.WriteBytes(buffer);

                                        await writ.StoreAsync();
                                        await writ.FlushAsync();
                                    }
                                    toSend.Content.Dispose();
                                });
                            }
                            else
                            {
                                writ.WriteString("HTTP/1.1 200 OK\r\n");
                                writ.WriteString("Content-Type: text/html\r\n");
                                writ.WriteString("Content-Length: 0\r\n");
                                writ.WriteString("Pragma: no-cache\r\n");
                                writ.WriteString("Connection: close\r\n");
                                writ.WriteString("\r\n");
                                Task.Run(async () => { await writ.StoreAsync(); });
                            }
                        }
                        break;
                    }
                }

                if (foundrule) return;

                writ.WriteString("HTTP/1.1 404 Not Found\r\n");
                writ.WriteString("Content-Type: text/html\r\n");
                writ.WriteString("Content-Length: 9\r\n");
                writ.WriteString("Pragma: no-cache\r\n");
                writ.WriteString("Connection: close\r\n");
                writ.WriteString("\r\n");
                writ.WriteString("Not found");
                Task.Run(async () => { await writ.StoreAsync(); });
            }
        }
    }
}