using System.Text;
using CategoryProduct.API.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CategoryProduct.API.Tests
{
    public class PickupDirectoryEmailSenderTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "cp-pickup-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
            catch (IOException) { }
        }

        private sealed class FakeEnv : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = "Test";
            public string ApplicationName { get; set; } = "Test";
            public string ContentRootPath { get; set; } = string.Empty;
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }

        private static PickupDirectoryEmailSender Create(string directory, string contentRoot = "", string? from = null) =>
            new(Options.Create(new EmailOptions { Enabled = true, PickupDirectory = directory, From = from ?? "noreply@categoryproduct.local" }),
                new FakeEnv { ContentRootPath = contentRoot },
                NullLogger<PickupDirectoryEmailSender>.Instance);

        private static string[] Files(string dir) => Directory.GetFiles(dir, "*.eml");

        [Fact]
        public async Task Send_CreatesMissingDirectory_AndWritesOneFile()
        {
            var dir = Path.Combine(_root, "nested", "outbox");
            await Create(dir).SendAsync(new EmailMessage("a@b.co", "Hello", "Body text"));

            var file = Assert.Single(Files(dir));
            Assert.Matches(@"^\d{8}T\d{9}-[0-9a-f]{32}\.eml$", Path.GetFileName(file));
        }

        [Fact]
        public async Task Send_TwoMessages_GetUniqueFileNames()
        {
            var dir = Path.Combine(_root, "out");
            var sender = Create(dir);
            var m = new EmailMessage("a@b.co", "Same", "Same body");
            await sender.SendAsync(m);
            await sender.SendAsync(m);

            var names = Files(dir).Select(Path.GetFileName).ToList();
            Assert.Equal(2, names.Count);
            Assert.Equal(2, names.Distinct().Count());
        }

        [Fact]
        public async Task Send_WritesHeadersAndBody_WithCrLf()
        {
            var dir = Path.Combine(_root, "out");
            await Create(dir, from: "shop@categoryproduct.local")
                .SendAsync(new EmailMessage("a@b.co", "Order #5 placed", "Line one\nLine two"));

            var text = await File.ReadAllTextAsync(Assert.Single(Files(dir)), Encoding.UTF8);
            var headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            Assert.True(headerEnd > 0);
            var headers = text[..headerEnd];
            Assert.Contains("From: shop@categoryproduct.local", headers);
            Assert.Contains("To: a@b.co", headers);
            Assert.Contains("Subject: Order #5 placed", headers);
            Assert.Contains("Date: ", headers);
            Assert.Matches(@"Message-ID: <[0-9a-f]{32}@categoryproduct\.local>", headers);
            Assert.Contains("MIME-Version: 1.0", headers);
            Assert.Contains("Content-Type: text/plain; charset=utf-8", headers);
            Assert.Contains("Content-Transfer-Encoding: 8bit", headers);
            Assert.Equal("Line one\r\nLine two", text[(headerEnd + 4)..]);
            Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
        }

        [Fact]
        public async Task Send_Utf8Body_RoundTrips_WithoutBom()
        {
            var dir = Path.Combine(_root, "out");
            var body = "Grüße – مرحبا \U0001F600";
            await Create(dir).SendAsync(new EmailMessage("a@b.co", "Hi", body));

            var path = Assert.Single(Files(dir));
            var bytes = await File.ReadAllBytesAsync(path);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
            var text = Encoding.UTF8.GetString(bytes);
            Assert.EndsWith(body, text);
        }

        [Fact]
        public async Task Send_NonAsciiSubject_IsEncoded()
        {
            var dir = Path.Combine(_root, "out");
            await Create(dir).SendAsync(new EmailMessage("a@b.co", "Low stock: Café ☕", "x"));

            var text = await File.ReadAllTextAsync(Assert.Single(Files(dir)));
            var headerBlock = text[..text.IndexOf("\r\n\r\n", StringComparison.Ordinal)];
            Assert.All(headerBlock, c => Assert.True(c < 0x80));
            var start = headerBlock.IndexOf("Subject: ", StringComparison.Ordinal) + "Subject: ".Length;
            var end = headerBlock.IndexOf("\r\nDate: ", StringComparison.Ordinal);
            var encoded = headerBlock[start..end];
            Assert.Contains("=?utf-8?B?", encoded);
            var decoded = string.Concat(encoded.Split("\r\n ").Select(w =>
                Encoding.UTF8.GetString(Convert.FromBase64String(w["=?utf-8?B?".Length..^2]))));
            Assert.Equal("Low stock: Café ☕", decoded);
        }

        [Fact]
        public async Task Send_LongNonAsciiSubject_IsFoldedIntoShortLines()
        {
            var dir = Path.Combine(_root, "out");
            var subject = "Low stock: " + new string('é', 150);
            await Create(dir).SendAsync(new EmailMessage("a@b.co", subject, "x"));

            var text = await File.ReadAllTextAsync(Assert.Single(Files(dir)));
            var lines = text.Split("\r\n").TakeWhile(l => l.Length > 0).ToList();
            Assert.All(lines, l => Assert.True(l.Length <= 76, l));
        }

        [Fact]
        public async Task RelativeDirectory_ResolvesAgainstContentRoot_AbsoluteIsUsedAsIs()
        {
            var contentRoot = Path.Combine(_root, "content");
            await Create("App_Data/email-outbox", contentRoot).SendAsync(new EmailMessage("a@b.co", "S", "B"));
            Assert.Single(Files(Path.Combine(contentRoot, "App_Data", "email-outbox")));

            var absolute = Path.Combine(_root, "absolute-dir");
            await Create(absolute, Path.Combine(_root, "ignored")).SendAsync(new EmailMessage("a@b.co", "S", "B"));
            Assert.Single(Files(absolute));
            Assert.False(Directory.Exists(Path.Combine(_root, "ignored")));
        }

        [Theory]
        [InlineData("Hello\r\nBcc: evil@x.co")]
        [InlineData("Hello\nBcc: evil@x.co")]
        public async Task Send_SubjectWithLineBreak_IsRejected_AndNothingIsWritten(string subject)
        {
            var dir = Path.Combine(_root, "out");
            var message = new EmailMessage("a@b.co", subject, "x");
            Assert.NotNull(message.Validate());

            await Assert.ThrowsAsync<InvalidOperationException>(() => Create(dir).SendAsync(message));
            Assert.False(Directory.Exists(dir) && Files(dir).Length > 0);
        }

        [Fact]
        public async Task Send_RecipientWithLineBreak_IsRejected()
        {
            var message = new EmailMessage("a@b.co\r\nBcc: evil@x.co", "S", "B");
            Assert.NotNull(message.Validate());
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Create(Path.Combine(_root, "out")).SendAsync(message));
        }

        [Fact]
        public async Task Send_UnusableDirectory_Throws()
        {
            // A path below an existing FILE can never become a directory.
            Directory.CreateDirectory(_root);
            var blocker = Path.Combine(_root, "blocker");
            await File.WriteAllTextAsync(blocker, "not a directory");

            await Assert.ThrowsAnyAsync<Exception>(() =>
                Create(Path.Combine(blocker, "sub")).SendAsync(new EmailMessage("a@b.co", "S", "B")));
        }

        [Theory]
        [InlineData("a@b.co", "S", "B", true)]
        [InlineData("", "S", "B", false)]
        [InlineData("not-an-address", "S", "B", false)]
        [InlineData("a@b.co", "", "B", false)]
        [InlineData("a@b.co", "S", "", false)]
        public void Validate_Basics(string to, string subject, string body, bool valid) =>
            Assert.Equal(valid, new EmailMessage(to, subject, body).Validate() == null);

        [Fact]
        public void Validate_SubjectLength_Limit200()
        {
            Assert.Null(new EmailMessage("a@b.co", new string('s', 200), "B").Validate());
            Assert.NotNull(new EmailMessage("a@b.co", new string('s', 201), "B").Validate());
        }

        [Fact]
        public void Validate_Reason_NeverContainsTheBody()
        {
            var reason = new EmailMessage("bad address", "S", "SECRET-BODY-TEXT").Validate();
            Assert.NotNull(reason);
            Assert.DoesNotContain("SECRET-BODY-TEXT", reason);
        }

        [Fact]
        public void SmtpMailMessage_IsPlainText_Utf8()
        {
            var options = new EmailOptions { From = "noreply@categoryproduct.local" };
            using var mail = SmtpEmailSender.CreateMailMessage(options, new EmailMessage("a@b.co", "S", "B"));
            Assert.False(mail.IsBodyHtml);
            Assert.Equal(Encoding.UTF8, mail.BodyEncoding);
            Assert.Equal("a@b.co", Assert.Single(mail.To).Address);
            Assert.Equal("noreply@categoryproduct.local", mail.From!.Address);
        }
    }
}
