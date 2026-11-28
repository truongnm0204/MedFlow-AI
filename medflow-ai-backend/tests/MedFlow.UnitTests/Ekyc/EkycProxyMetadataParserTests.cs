using MedFlow.Application.Services.Ekyc;

namespace MedFlow.UnitTests.Ekyc;

public sealed class EkycProxyMetadataParserTests
{
    // ---------------------------------------------------------------
    // addFile response hash extraction
    // ---------------------------------------------------------------

    [Fact]
    public void ExtractAddFileHash_ExtractsObjectHash()
    {
        const string body = """{"object":{"hash":"idg20261127/IDG01_9b2d050c-b657-48aa","fileName":"x"},"message":"ok"}""";

        var hash = EkycProxyMetadataParser.ExtractAddFileHash(body, out var warning);

        Assert.Equal("idg20261127/IDG01_9b2d050c-b657-48aa", hash);
        Assert.Null(warning);
    }

    [Fact]
    public void ExtractAddFileHash_ReturnsNull_WhenObjectMissing()
    {
        var hash = EkycProxyMetadataParser.ExtractAddFileHash("""{"message":"ok"}""", out var warning);

        Assert.Null(hash);
        Assert.Equal("missing_object_field", warning);
    }

    [Fact]
    public void ExtractAddFileHash_ReturnsNull_WhenHashMissing()
    {
        var hash = EkycProxyMetadataParser.ExtractAddFileHash("""{"object":{"fileName":"x"}}""", out var warning);

        Assert.Null(hash);
        Assert.Equal("missing_hash_field", warning);
    }

    [Fact]
    public void ExtractAddFileHash_ReturnsNull_OnMalformedJson()
    {
        var hash = EkycProxyMetadataParser.ExtractAddFileHash("{not json", out var warning);

        Assert.Null(hash);
        Assert.Equal("malformed_json", warning);
    }

    [Fact]
    public void ExtractAddFileHash_ReturnsNull_OnEmptyBody()
    {
        var hash = EkycProxyMetadataParser.ExtractAddFileHash("", out var warning);

        Assert.Null(hash);
        Assert.Equal("empty_body", warning);
    }

    [Fact]
    public void ExtractAddFileHash_RejectsOversizedHash()
    {
        var huge = new string('a', 300);
        var body = "{\"object\":{\"hash\":\"" + huge + "\"}}";

        var hash = EkycProxyMetadataParser.ExtractAddFileHash(body, out var warning);

        Assert.Null(hash);
        Assert.Equal("hash_field_invalid", warning);
    }

    // ---------------------------------------------------------------
    // inference request hash extraction
    // ---------------------------------------------------------------

    [Fact]
    public void ExtractRequestHashes_ExtractsImgAndImgFrontBack()
    {
        const string body = """{"img_front":"idg20261127/IDG01_front","img_back":"idg20261127/IDG01_back","client_session":"x"}""";

        var hashes = EkycProxyMetadataParser.ExtractRequestHashes(body, out var warning);

        Assert.Equal(2, hashes.Count);
        Assert.Contains("idg20261127/IDG01_front", hashes);
        Assert.Contains("idg20261127/IDG01_back", hashes);
        Assert.Null(warning);
    }

    [Fact]
    public void ExtractRequestHashes_ExtractsImgField()
    {
        const string body = """{"img":"idg20261127/IDG01_single","token":"x"}""";

        var hashes = EkycProxyMetadataParser.ExtractRequestHashes(body, out var warning);

        Assert.Single(hashes);
        Assert.Equal("idg20261127/IDG01_single", hashes[0]);
    }

    [Fact]
    public void ExtractRequestHashes_SkipsEmbeddedBase64()
    {
        // A value longer than MaxHashLength is embedded content, not a hash ref —
        // must be skipped entirely (metadata-only guarantee, never copied anywhere).
        var embedded = new string('A', 500);
        var body = "{\"img\":\"" + embedded + "\"}";

        var hashes = EkycProxyMetadataParser.ExtractRequestHashes(body, out _);

        Assert.Empty(hashes);
    }

    [Fact]
    public void ExtractRequestHashes_ReturnsEmpty_OnNonJsonBody()
    {
        // multipart form-data uploads are not JSON — expected, no warning.
        var hashes = EkycProxyMetadataParser.ExtractRequestHashes("------boundary\r\nContent-Disposition: form-data\r\n\r\nbinary", out var warning);

        Assert.Empty(hashes);
        Assert.Null(warning);
    }

    [Fact]
    public void ExtractRequestHashes_ReturnsEmpty_OnEmptyBody()
    {
        var hashes = EkycProxyMetadataParser.ExtractRequestHashes(null, out var warning);

        Assert.Empty(hashes);
        Assert.Null(warning);
    }

    [Fact]
    public void ExtractRequestHashes_Warns_OnNonObjectRoot()
    {
        var hashes = EkycProxyMetadataParser.ExtractRequestHashes("""["array"]""", out var warning);

        Assert.Empty(hashes);
        Assert.Equal("root_not_object", warning);
    }

    // ---------------------------------------------------------------
    // compare document hash extraction (binding validation prep)
    // ---------------------------------------------------------------

    [Fact]
    public void ExtractCompareDocumentHash_PrefersImgFront()
    {
        const string body = """{"img_front":"idg20261127/IDG01_front","img_back":"idg20261127/IDG01_back","img":"idg20261127/IDG01_face"}""";

        var hash = EkycProxyMetadataParser.ExtractCompareDocumentHash(body, out var warning);

        Assert.Equal("idg20261127/IDG01_front", hash);
        Assert.Null(warning);
    }

    [Fact]
    public void ExtractCompareDocumentHash_FallsBackToImgBack()
    {
        const string body = """{"img_back":"idg20261127/IDG01_back","img":"idg20261127/IDG01_face"}""";

        var hash = EkycProxyMetadataParser.ExtractCompareDocumentHash(body, out _);

        Assert.Equal("idg20261127/IDG01_back", hash);
    }

    [Fact]
    public void ExtractCompareDocumentHash_FallsBackToImg()
    {
        const string body = """{"img":"idg20261127/IDG01_doc"}""";

        var hash = EkycProxyMetadataParser.ExtractCompareDocumentHash(body, out _);

        Assert.Equal("idg20261127/IDG01_doc", hash);
    }

    [Fact]
    public void ExtractCompareDocumentHash_ReturnsNull_WhenNoDocumentRef()
    {
        // fail-closed: caller must treat null as UNVERIFIABLE_BINDING
        const string body = """{"client_session":"x","token":"y"}""";

        var hash = EkycProxyMetadataParser.ExtractCompareDocumentHash(body, out var warning);

        Assert.Null(hash);
        Assert.Equal("missing_compare_document_ref", warning);
    }

    [Fact]
    public void ExtractCompareDocumentHash_ReturnsNull_OnMalformedJson()
    {
        var hash = EkycProxyMetadataParser.ExtractCompareDocumentHash("{broken", out var warning);

        Assert.Null(hash);
        Assert.Equal("malformed_compare_json", warning);
    }

    [Fact]
    public void ExtractCompareDocumentHash_ReturnsNull_OnEmptyBody()
    {
        var hash = EkycProxyMetadataParser.ExtractCompareDocumentHash(null, out var warning);

        Assert.Null(hash);
        Assert.Equal("empty_compare_body", warning);
    }

    [Fact]
    public void ExtractCompareDocumentHash_SkipsEmbeddedBase64()
    {
        var embedded = new string('B', 400);
        var body = "{\"img_front\":\"" + embedded + "\",\"img\":\"idg20261127/IDG01_face\"}";

        var hash = EkycProxyMetadataParser.ExtractCompareDocumentHash(body, out _);

        // img_front skipped (embedded content), falls back to img
        Assert.Equal("idg20261127/IDG01_face", hash);
    }
}
