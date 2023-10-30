namespace tx_viewer_pdf_signaturelines.Models
{
    public class Envelope
    {
        public string DocumentId { get; set; }
        public string SignatureHash { get; set; }
        public string SignatureData { get; set; }
    }

}
