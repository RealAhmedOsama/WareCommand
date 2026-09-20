using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Wms.Infrastructure.Backups;

namespace Wms.Infrastructure.Tests.Backups;

public sealed class WmsBackupArchiveTests
{
    [Fact]
    public async Task EncryptedArchiveRoundTripsAndRejectsTampering()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "warecommand-backup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var input = Path.Combine(root, "input.zip");
        var encrypted = Path.Combine(root, "backup.wcbak");
        var extracted = Path.Combine(root, "extracted");
        var tampered = Path.Combine(root, "tampered.wcbak");
        var key = RandomNumberGenerator.GetBytes(32);

        try
        {
            await using (var archive = ZipFile.Open(input, ZipArchiveMode.Create))
            await using (var content = new MemoryStream(Encoding.UTF8.GetBytes("backup payload")))
            {
                var entry = archive.CreateEntry("database.dump");
                await using var entryStream = entry.Open();
                await content.CopyToAsync(entryStream);
            }

            await WmsBackupArchive.EncryptAsync(input, encrypted, key, CancellationToken.None);
            var zipPath = await WmsBackupArchive.DecryptAsync(
                encrypted,
                extracted,
                key,
                CancellationToken.None);
            using (var restored = ZipFile.OpenRead(zipPath))
            using (var reader = new StreamReader(restored.GetEntry("database.dump")!.Open()))
            {
                (await reader.ReadToEndAsync()).Should().Be("backup payload");
            }

            File.Copy(encrypted, tampered);
            var bytes = await File.ReadAllBytesAsync(tampered);
            bytes[^1] ^= 0x01;
            await File.WriteAllBytesAsync(tampered, bytes);

            var act = () => WmsBackupArchive.DecryptAsync(
                tampered,
                Path.Combine(root, "tampered-extracted"),
                key,
                CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
