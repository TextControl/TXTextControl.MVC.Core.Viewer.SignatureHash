using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using TXTextControl;
using tx_viewer_pdf_signaturelines.Models;

namespace tx_viewer_pdf_signaturelines.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        [HttpPost]
        public IActionResult ProcessSignature([FromBody] TXTextControl.Web.MVC.DocumentViewer.Models.SignatureData data)
        {
            if (data == null)
            {
                return BadRequest();
            }

            string signatureDataJson = JsonConvert.SerializeObject(data.SignatureLines);
            string hash = CreateHash(signatureDataJson);

            Envelope envelope = new Envelope
            {
                DocumentId = data.UniqueId,
                SignatureHash = hash,
                SignatureData = signatureDataJson
            };

            AddEnvelope(envelope);

            if (SaveSignedPDF(data, envelope))
            {
                return Ok(true);
            }
            else
            {
                return StatusCode(500);
            }
        }

        public IActionResult Validate([FromForm] IFormFile file)
        {
            if (file == null)
            {
                return NotFound();
            }

            byte[] bPDF = GetBytesFromFormFile(file);
            var uploadedEnvelope = ExtractEnvelopeFromPDF(bPDF);

            if (uploadedEnvelope == null)
            {
                return NotFound();
            }

            var envelopes = LoadEnvelopesFromJson();
            var envelope = envelopes.FirstOrDefault(e => e.DocumentId == uploadedEnvelope.DocumentId);

            if (envelope != null && envelope.SignatureHash == uploadedEnvelope.SignatureHash)
            {
                return View(true);
            }

            return View(false);
        }

        public IActionResult Index()
        {
            return View(LoadEnvelopesFromJson());
        }

        public IActionResult Sign()
        {
            return View();
        }

        public IActionResult Download(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            var filePath = Path.Combine("App_Data/signed", $"{id}.pdf");

            if (System.IO.File.Exists(filePath))
            {
                var fileBytes = System.IO.File.ReadAllBytes(filePath);
                return File(fileBytes, "application/pdf", $"{id}.pdf");
            }
            else
            {
                return NotFound();
            }
        }

        private byte[] GetBytesFromFormFile(IFormFile file)
        {
            using (var memoryStream = new MemoryStream())
            {
                file.CopyTo(memoryStream);
                return memoryStream.ToArray();
            }
        }

        private void AddEnvelope(Envelope envelope)
        {
            var filePath = "App_Data/envelopes.json";
            List<Envelope> envelopes = LoadEnvelopesFromJson();
            envelopes.Add(envelope);
            System.IO.File.WriteAllText(filePath, JsonConvert.SerializeObject(envelopes));
        }

        private List<Envelope> LoadEnvelopesFromJson()
        {
            var filePath = "App_Data/envelopes.json";
            if (System.IO.File.Exists(filePath))
            {
                var json = System.IO.File.ReadAllText(filePath);
                return JsonConvert.DeserializeObject<List<Envelope>>(json);
            }
            return new List<Envelope>();
        }

        private bool SaveSignedPDF(TXTextControl.Web.MVC.DocumentViewer.Models.SignatureData data, Envelope envelope)
        {
            try
            {
                using (TXTextControl.ServerTextControl tx = new TXTextControl.ServerTextControl())
                {
                    tx.Create();
                    tx.Load(Convert.FromBase64String(data.SignedDocument.Document), TXTextControl.BinaryStreamType.InternalUnicodeFormat);

                    var embeddedFile = new EmbeddedFile($"tx-hash_{data.UniqueId}.txt", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope)), null)
                    {
                        Relationship = "Data",
                        MIMEType = "application/json"
                    };

                    X509Certificate2 cert = new X509Certificate2("App_Data/textcontrolself.pfx", "123");

                    var saveSettings = new TXTextControl.SaveSettings
                    {
                        EmbeddedFiles = new EmbeddedFile[] { embeddedFile },
                        CreatorApplication = "TX Text Control Sample Application",
                        SignatureFields = new DigitalSignature[]
                        {
                            new TXTextControl.DigitalSignature(cert, null, "txsign")
                        }
                    };

                    var savePath = Path.Combine("App_Data/signed", $"{data.UniqueId}.pdf");
                    Directory.CreateDirectory(Path.GetDirectoryName(savePath));
                    tx.Save(savePath, TXTextControl.StreamType.AdobePDF, saveSettings);
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private string CreateHash(string json)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(json));
                return BitConverter.ToString(bytes).Replace("-", "").ToLower();
            }
        }

        private Envelope ExtractEnvelopeFromPDF(byte[] document)
        {
            using (TXTextControl.ServerTextControl tx = new TXTextControl.ServerTextControl())
            {
                tx.Create();

                var loadSettings = new TXTextControl.LoadSettings
                {
                    EmbeddedFiles = new EmbeddedFile[] { },
                };

                tx.Load(document, TXTextControl.BinaryStreamType.AdobePDF, loadSettings);

                var embeddedFile = loadSettings.EmbeddedFiles
                    .FirstOrDefault(ef => ef.FileName.StartsWith("tx-hash_"));

                if (embeddedFile != null)
                {
                    return JsonConvert.DeserializeObject<Envelope>(Encoding.UTF8.GetString((byte[])embeddedFile.Data));
                }
            }

            return null;
        }
    }
}
