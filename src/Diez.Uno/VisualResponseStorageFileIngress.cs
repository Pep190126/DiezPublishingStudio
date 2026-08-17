using Windows.Storage;

namespace DiezPublishingStudio.UnoSpike;

/// <summary>
/// File pickers return a StorageFile, which is the authoritative handle to the bytes selected by
/// the user. On desktop targets its Path can be virtualized or otherwise unsuitable for handing
/// directly to System.IO code. Stage the picked bytes into a private temporary file first, then
/// let the existing audited Response importer operate on that stable local copy.
/// </summary>
internal static class VisualResponseStorageFileIngress
{
    public static async Task<VisualResponseImportResult> ImportManualVisualResponsePackAsync(
        this DiezProjectDocument document,
        StorageFile pickedFile)
    {
        ArgumentNullException.ThrowIfNull(pickedFile);

        var stagedPath = Path.Combine(
            Path.GetTempPath(),
            "DiezPickedResponse-" + Guid.NewGuid().ToString("N") + ".zip");

        try
        {
            await using (var source = await pickedFile.OpenStreamForReadAsync())
            await using (var destination = new FileStream(
                stagedPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                useAsync: true))
            {
                await source.CopyToAsync(destination);
                await destination.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            try { if (File.Exists(stagedPath)) File.Delete(stagedPath); } catch { }
            return new(
                false,
                0,
                0,
                0,
                $"Response non importato [PICKER_READ_FAILED]: impossibile leggere i byte del file selezionato '{pickedFile.Name}': {ex.GetBaseException().Message}",
                string.Empty);
        }

        try
        {
            return await VisualBookDocumentAdapter.ImportManualVisualResponsePackAsync(document, stagedPath);
        }
        finally
        {
            try { if (File.Exists(stagedPath)) File.Delete(stagedPath); } catch { }
        }
    }
}
