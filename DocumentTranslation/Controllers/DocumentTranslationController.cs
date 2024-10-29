using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Text.Json;
using Swashbuckle.AspNetCore.Annotations;
using System.Text.Json.Serialization;

namespace WebTranslate20241026.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class DocumentTranslationController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly string subscriptionKey;
        private readonly string endpoint;
        private readonly string region;
        private readonly HttpClient _httpClient;

        public DocumentTranslationController(IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            subscriptionKey = _configuration["TRANSLATOR_TEXT_SUBSCRIPTION_KEY"];
            endpoint = _configuration["TRANSLATOR_TEXT_ENDPOINT"];
            region = _configuration["TRANSLATOR_TEXT_REGION"];
            _httpClient = httpClientFactory.CreateClient();
        }

        [HttpPost]
        [Route("TranslateDocument")]
        [SwaggerOperation(
            Summary = "Translate an uploaded document from English to Japanese",
            Description = "Translates a document from English to Japanese using Azure Translator."
        )]
        [SwaggerResponse(200, "Successful translation")]
        [SwaggerResponse(400, "Bad request")]
        [SwaggerResponse(500, "Internal server error")]
        public async Task<IActionResult> TranslateDocument([FromBody] OpenAIFileIdRefs openAIFileIdRefs)
        {
            if (openAIFileIdRefs == null || openAIFileIdRefs.FileIdRefs == null || openAIFileIdRefs.FileIdRefs.Count == 0)
            {
                return BadRequest("No files provided.");
            }

            var fileRef = openAIFileIdRefs.FileIdRefs[0]; // 複数ファイルの場合はループ処理
            var downloadLink = fileRef.DownloadLink;

            if (string.IsNullOrEmpty(downloadLink))
            {
                return BadRequest("Download link is missing.");
            }

            // ファイルをダウンロード
            byte[] fileBytes;
            try
            {
                fileBytes = await _httpClient.GetByteArrayAsync(downloadLink);
            }
            catch (Exception ex)
            {
                return BadRequest($"Failed to download file: {ex.Message}");
            }

            // 一時ファイルに保存
            var tempFilePath = Path.GetTempFileName();
            await System.IO.File.WriteAllBytesAsync(tempFilePath, fileBytes);

            // ファイルストリームを作成
            using var fileStream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read);

            // 翻訳処理
            string sourceLanguage = "en";
            string targetLanguage = "ja";
            string route = $"/translator/document:translate?api-version=2024-05-01&sourceLanguage={sourceLanguage}&targetLanguage={targetLanguage}";

            using (var content = new MultipartFormDataContent())
            {
                var fileContent = new StreamContent(fileStream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(fileRef.MimeType);
                content.Add(fileContent, "document", fileRef.Name);

                // ヘッダーの設定
                _httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", subscriptionKey);
                _httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Region", region);

                // リクエストの送信
                var response = await _httpClient.PostAsync(endpoint + route, content);

                if (response.IsSuccessStatusCode)
                {
                    // 翻訳済みファイルを取得
                    var responseBytes = await response.Content.ReadAsByteArrayAsync();

                    // Base64エンコード
                    var base64Content = Convert.ToBase64String(responseBytes);

                    // GPTsに返すレスポンスを作成
                    var fileResponse = new OpenAIFileResponse
                    {
                        OpenAIFileResponseList = new System.Collections.Generic.List<OpenAIFileResponseItem>
                    {
                        new OpenAIFileResponseItem
                        {
                            Name = "translated_" + fileRef.Name,
                            MimeType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream",
                            Content = base64Content
                        }
                    }
                    };

                    return Ok(fileResponse);
                }
                else
                {
                    // エラーメッセージを取得
                    var error = await response.Content.ReadAsStringAsync();
                    return StatusCode((int)response.StatusCode, new ErrorResponse { Message = "Translation failed.", Details = error });
                }
            }
        }
    }

    // リクエスト用モデル
    public class OpenAIFileIdRefs
    {
        [JsonPropertyName("openaiFileIdRefs")]
        public System.Collections.Generic.List<OpenAIFileIdRef> FileIdRefs { get; set; }
    }

    public class OpenAIFileIdRef
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("mime_type")]
        public string MimeType { get; set; }

        [JsonPropertyName("download_link")]
        public string DownloadLink { get; set; }
    }

    // レスポンス用モデル
    public class OpenAIFileResponse
    {
        [JsonPropertyName("openaiFileResponse")]
        public System.Collections.Generic.List<OpenAIFileResponseItem> OpenAIFileResponseList { get; set; }
    }

    public class OpenAIFileResponseItem
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("mime_type")]
        public string MimeType { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; }
    }

    // エラーレスポンスモデル
    public class ErrorResponse
    {
        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("details")]
        public string Details { get; set; }
    }
}