using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NEON.Trader.Desktop.Models;
using NEON.Trader.Desktop.Services;
using Windows.System;

namespace NEON.Trader.Desktop.Models
{
    public sealed class ChatMessageVm
    {
        public string Role { get; set; } = "user"; // "user" | "agent"
        public string Body { get; set; } = "";
        public string RoleLabel => Role == "user" ? "YOU" : "AGENT";
        public Brush RoleBrush { get; set; } = null!;
    }
}

namespace NEON.Trader.Desktop.Views
{
    public sealed partial class AgentPage : Page
    {
        private readonly AppServices _services = App.Services;
        private readonly ObservableCollection<ChatMessageVm> _messages = new();
        private readonly List<ChatHistoryItem> _history = new();
        private bool _busy;

        public AgentPage()
        {
            InitializeComponent();
            ChatList.ItemsSource = _messages;
            StateText.Text = "idle";
        }

        private void Input_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter && !_busy)
            {
                e.Handled = true;
                _ = SendAsync();
            }
        }

        private void Send_Click(object sender, object e)
        {
            if (!_busy) _ = SendAsync();
        }

        private async Task SendAsync()
        {
            var text = (InputBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(text)) return;

            var neon = (Brush)Application.Current.Resources["NeonBrush"];
            var warn = (Brush)Application.Current.Resources["WarnBrush"];
            var down = (Brush)Application.Current.Resources["DownBrush"];

            _messages.Add(new ChatMessageVm { Role = "user", Body = text, RoleBrush = neon });
            InputBox.Text = "";
            ErrorText.Text = "";
            _busy = true;
            SendBtn.IsEnabled = false;
            StateText.Text = "thinking...";

            try
            {
                var req = new ChatRequest
                {
                    Message = text,
                    History = _history.TakeLast(10).ToList(),
                };

                using var startCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var accepted = await _services.Api.StartChatAsync(req, startCts.Token);
                if (string.IsNullOrEmpty(accepted.JobId))
                    throw new Exception("No job id returned");

                var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(5);
                ChatJob? job = null;
                while (DateTime.UtcNow < deadline)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                    using var pollCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    job = await _services.Api.GetChatJobAsync(accepted.JobId!, pollCts.Token);
                    var s = (job.Status ?? "").ToLowerInvariant();
                    if (s == "completed" || s == "failed" || s == "error") break;
                }

                if (job is null)
                    throw new Exception("Chat job timed out");

                if ((job.Status ?? "").Equals("completed", StringComparison.OrdinalIgnoreCase))
                {
                    var reply = job.Reply ?? "";
                    _messages.Add(new ChatMessageVm { Role = "agent", Body = reply, RoleBrush = warn });
                    _history.Add(new ChatHistoryItem { Role = "user",      Content = text });
                    _history.Add(new ChatHistoryItem { Role = "assistant", Content = reply });
                    StateText.Text = "idle";
                }
                else
                {
                    var err = job.Error ?? job.Status ?? "unknown error";
                    _messages.Add(new ChatMessageVm { Role = "agent", Body = "[error] " + err, RoleBrush = down });
                    StateText.Text = "error";
                }
            }
            catch (Exception ex)
            {
                ErrorText.Text = ex.Message;
                (App.MainWindow as MainWindow)?.Toast(ex.Message, error: true);
                StateText.Text = "error";
            }
            finally
            {
                _busy = false;
                SendBtn.IsEnabled = true;
                ScrollToEnd();
            }
        }

        private void ScrollToEnd()
        {
            if (_messages.Count > 0)
                ChatList.ScrollIntoView(_messages[^1]);
        }
    }
}
