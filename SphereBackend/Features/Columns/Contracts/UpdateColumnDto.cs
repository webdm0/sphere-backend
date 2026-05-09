using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SphereBackend.Features.Columns
{
    public class UpdateColumnDto
    {
        private string? _title;
        [JsonIgnore]
        public bool TitleSpecified { get; private set; }

        [MinLength(1, ErrorMessage = "Title is required.")]
        [MaxLength(32, ErrorMessage = "Title must be 32 characters or fewer.")]
        [JsonPropertyName("title")]
        public string? Title
        {
            get => _title;
            set
            {
                TitleSpecified = true;
                _title = value;
            }
        }

        private bool? _isArchived;
        [JsonIgnore]
        public bool IsArchivedSpecified { get; private set; }

        [JsonPropertyName("isArchived")]
        public bool? IsArchived
        {
            get => _isArchived;
            set
            {
                IsArchivedSpecified = true;
                _isArchived = value;
            }
        }
    }
}
