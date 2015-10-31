using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Networking;
using Windows.Networking.Connectivity;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml.Media;
using DiscoLight.Annotations;

namespace DiscoLight
{
    public sealed partial class MainPage : INotifyPropertyChanged
    {
        private string _ipAddress;
        private SolidColorBrush _lightColor = new SolidColorBrush(Colors.White);
        private int _port = 8081;
        private WebServer _webServer;

        public MainPage()
        {
            InitializeComponent();
            StartServer();
        }

        public SolidColorBrush LightColor
        {
            get { return _lightColor; }
            set
            {
                _lightColor = value;
                OnPropertyChanged();
            }
        }

        public string IpAddress
        {
            get { return _ipAddress; }
            set
            {
                _ipAddress = value;
                OnPropertyChanged();
            }
        }

        public int Port
        {
            get { return _port; }
            set
            {
                _port = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void StartServer()
        {
            var found = false;

            // Get all network adapters
            var adapters = NetworkInformation.GetHostNames();
            if (adapters.Count == 0)
            {
                Debug.WriteLine("Turn on WiFi");
            }
            else
            {
                foreach (
                    var adapter in
                        adapters.Where(
                            adapter =>
                                adapter.IPInformation != null &&
                                (
                                    (
                                        adapter.IPInformation.NetworkAdapter.IanaInterfaceType == 71 || // An IEEE 802.11 wireless network interface
                                        adapter.IPInformation.NetworkAdapter.IanaInterfaceType == 6     // An Ethernet network interface
                                    ) &&
                                    (
                                        adapter.Type == HostNameType.Ipv4 || 
                                        adapter.Type == HostNameType.Ipv6
                                    )
                                )
                            )
                )
                {
                    // If found assign it's IP to a variable
                    found = true;
                    IpAddress = adapter.RawName;
                    break;
                }

                if (found)
                {
                    // create a new dictionary for server rules
                    var rules = new Dictionary<Regex, RuleDeletage>
                    {
                        // add a rule for homepage: URL /
                        // will fire method "homePage" when triggered
                        {
                            new Regex("^/$"),
                            HomePage
                        },

                        // Add the API
                        {
                            new Regex(@"^\/\?color=[a-f0-9]*$", RegexOptions.IgnoreCase),
                            ApiPage
                        }
                    };

                    // With the set of rules and IP of the network adapter create a new web server object and 
                    // assign an error event method
                    _webServer = new WebServer(rules, IpAddress, Port.ToString());
                    _webServer.ErrorOccured += WebServerOnErrorOccured;

                    Debug.WriteLine("Server listening to: http://" + IpAddress + ":" + Port + "/");
                }
                else
                {
                    Debug.WriteLine("Turn on WiFi");
                }
            }
        }

        private async Task<WebResponse> HomePage(WebRequest request)
        {
            // Prepare the response object
            var response = new WebResponse
            {
                // Create a new dictionary for headers - this could be done using a more advanced class for 
                // WebResponse object - I just used a simple structure
                Header = new Dictionary<string, string>
                {
                    // Add content type header
                    {
                        "Content-Type",
                        "application/json'"
                    }
                }
            };

            // Build the response content
            Stream responseText = new MemoryStream();
            var contentWriter = new StreamWriter(responseText);
            contentWriter.WriteLine(
                "{\"status\":200,\"message\":\"The API is available at http://" + IpAddress + ":" + Port +
                "/?color=######\"}"
                );
            contentWriter.Flush();

            // Assign the response
            response.Content = responseText;

            // Return the response
            return response;
        }


        private async Task<WebResponse> ApiPage(WebRequest request)
        {
            // Prepare the response object
            var response = new WebResponse
            {
                // Create a new dictionary for headers - this could be done using a more advanced class for 
                // WebResponse object - I just used a simple structure
                Header = new Dictionary<string, string>
                {
                    // Add content type header
                    {
                        "Content-Type",
                        "application/json'"
                    }
                }
            };

            Stream responseText = new MemoryStream();

            if (request.Uri.IndexOf('?') > -1)
            {
                var queryString = request.Uri.Substring(request.Uri.IndexOf('?'));

                var decoder = new WwwFormUrlDecoder(queryString);

                foreach (var color in decoder.Where(param => param.Name == "color").Select(param => Color.FromArgb(255,
                    Convert.ToByte(param.Value.Substring(0, 2), 16),
                    Convert.ToByte(param.Value.Substring(2, 2), 16),
                    Convert.ToByte(param.Value.Substring(4, 2), 16))))
                {
                    try
                    {
                        await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                            () =>
                            {
                                var colorBrush = new SolidColorBrush(color);
                                LightColor = colorBrush;

                                var contentWriter = new StreamWriter(responseText);
                                contentWriter.WriteLine(
                                    "{\"status\":200,\"message\":\"Color was set to " + LightColor.Color + "\"}"
                                    );
                                contentWriter.Flush();
                            }
                            );
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e);
                    }
                }
            }

            // Assign the response
            response.Content = responseText;

            // Return the response
            return response;
        }

        private static void WebServerOnErrorOccured(int code, string message)
        {
            Debug.WriteLine("WebServer Error: " + message);
        }

        [NotifyPropertyChangedInvocator]
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}