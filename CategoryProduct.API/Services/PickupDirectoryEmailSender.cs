using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace CategoryProduct.API.Services
{
    /// <summary>Writes each message as a UTF-8 .eml file in a pickup folder.</summary>
    public sealed partial class PickupDirectoryEmailSender : IEmailSender
    {
        private readonly string _directory;
        private readonly string _from;
        private readonly ILogger<PickupDirectoryEmailSender> _logger;

        public PickupDirectoryEmailSender(IOptions<EmailOptions> options, IHostEnvironment env,
            ILogger<PickupDirectoryEmailSender> logger)
        {
            _logger = logger;
            var p = options.Value.EffectivePickupDirectory;
            _directory = Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(env.ContentRootPath, p));
            _from = options.Value.EffectiveFrom.Replace("\r", "").Replace("\n", "");
        }

        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            var problem = message.Validate();
            if (problem != null)
                throw new InvalidOperationException("Email message rejected: " + problem);

            Directory.CreateDirectory(_directory);
            var fileName = $"{DateTime.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}.eml";
            var path = Path.Combine(_directory, fileName);
            var bytes = new UTF8Encoding(false).GetBytes(Build(message));
            try
            {
                await using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await fs.WriteAsync(bytes, cancellationToken);
            }
            catch
            {
                try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
                throw;
            }
            _logger.LogInformation("Email written to {FileName}", fileName);
        }

        private string Build(EmailMessage m)
        {
            var sb = new StringBuilder();
            sb.Append("From: ").Append(_from).Append("\r\n");
            sb.Append("To: ").Append(m.To).Append("\r\n");
            sb.Append("Subject: ").Append(EncodeSubject(m.Subject)).Append("\r\n");
            sb.Append("Date: ").Append(DateTime.UtcNow.ToString("R", CultureInfo.InvariantCulture)).Append("\r\n");
            sb.Append("Message-ID: <").Append(Guid.NewGuid().ToString("N")).Append("@categoryproduct.local>\r\n");
            sb.Append("MIME-Version: 1.0\r\n");
            sb.Append("Content-Type: text/plain; charset=utf-8\r\n");
            sb.Append("Content-Transfer-Encoding: 8bit\r\n");
            sb.Append("\r\n");
            sb.Append(LineBreaks().Replace(m.Body, "\r\n"));
            return sb.ToString();
        }

        /// <summary>RFC 2047: ASCII subjects are written as is; others as folded UTF-8 base64 encoded words.</summary>
        internal static string EncodeSubject(string subject)
        {
            if (subject.All(c => c < 0x80)) return subject;
            var words = new List<string>();
            var chunk = new StringBuilder();
            var chunkBytes = 0;
            foreach (var rune in subject.EnumerateRunes())
            {
                var n = rune.Utf8SequenceLength;
                if (chunkBytes + n > 36)
                {
                    words.Add(Word(chunk.ToString()));
                    chunk.Clear();
                    chunkBytes = 0;
                }
                chunk.Append(rune.ToString());
                chunkBytes += n;
            }
            if (chunk.Length > 0) words.Add(Word(chunk.ToString()));
            return string.Join("\r\n ", words);

            static string Word(string s) => "=?utf-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(s)) + "?=";
        }

        [GeneratedRegex("\r\n|\r|\n")]
        private static partial Regex LineBreaks();
    }
}
