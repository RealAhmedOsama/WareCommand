using FluentAssertions;
using Wms.Application.Attachments;
using Wms.Infrastructure.Attachments;

namespace Wms.Infrastructure.Tests.Attachments;

public sealed class LocalAttachmentStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "warecommand-local-attachment-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Generated_key_is_stored_and_traversal_key_is_rejected()
    {
        var storage = new LocalAttachmentStorage(new AttachmentStorageOptions
        {
            RootPath = _root,
            MaximumFileSizeBytes = 10_000,
            MaximumAttachmentsPerReference = 20,
            MaximumRequestBodyBytes = 12_000
        });
        await storage.StoreAsync(new AttachmentStorageWrite(
            "7/receipt/abc123",
            new MemoryStream("payload"u8.ToArray()),
            7,
            "text/plain"));

        var content = await storage.OpenReadAsync("7/receipt/abc123");
        using (content)
        using (var reader = new StreamReader(content))
        {
            (await reader.ReadToEndAsync()).Should().Be("payload");
        }

        var act = () => storage.OpenReadAsync("../outside");
        await act.Should().ThrowAsync<InvalidOperationException>();
        File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "outside")).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
