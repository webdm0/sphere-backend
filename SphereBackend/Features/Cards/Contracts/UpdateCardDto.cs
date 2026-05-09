using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SphereBackend.Features.Cards
{
    public class UpdateCardDto
    {
        private string? _assigneeId;
        [JsonIgnore]
        public bool AssigneeIdSpecified { get; private set; }

        [JsonPropertyName("assigneeId")]
        public string? AssigneeId
        {
            get => _assigneeId;
            set
            {
                AssigneeIdSpecified = true;
                _assigneeId = value;
            }
        }

        private string? _title;
        [JsonIgnore]
        public bool TitleSpecified { get; private set; }

        [MinLength(1, ErrorMessage = "Title is required.")]
        [MaxLength(80, ErrorMessage = "Title must be 80 characters or fewer.")]
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

        private string? _content;
        [JsonIgnore]
        public bool ContentSpecified { get; private set; }

        [JsonPropertyName("content")]
        public string? Content
        {
            get => _content;
            set
            {
                ContentSpecified = true;
                _content = value;
            }
        }

        private string? _priority;
        [JsonIgnore]
        public bool PrioritySpecified { get; private set; }

        [JsonPropertyName("priority")]
        public string? Priority
        {
            get => _priority;
            set
            {
                PrioritySpecified = true;
                _priority = value;
            }
        }

        private DateOnly? _startAt;
        [JsonIgnore]
        public bool StartAtSpecified { get; private set; }

        [JsonPropertyName("startAt")]
        public DateOnly? StartAt
        {
            get => _startAt;
            set
            {
                StartAtSpecified = true;
                _startAt = value;
            }
        }

        private DateOnly? _dueAt;
        [JsonIgnore]
        public bool DueAtSpecified { get; private set; }

        [JsonPropertyName("dueAt")]
        public DateOnly? DueAt
        {
            get => _dueAt;
            set
            {
                DueAtSpecified = true;
                _dueAt = value;
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
