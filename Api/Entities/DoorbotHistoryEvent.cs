using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;

namespace KoenZomers.Ring.Api.Entities
{
    public class DoorbotHistoryEvent
    {
        /// <summary>
        /// Unique identifier of this historical event
        /// </summary>
        [JsonPropertyName("id")]
        public long? Id { get; set; }

        /// <summary>
        /// Raw date time string when this event occurred
        /// </summary>
        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; }

        /// <summary>
        /// DateTime at which this event occurred 
        /// </summary>
        private DateTime? _createdAtDateTime;
        public DateTime? CreatedAtDateTime
        {
            get
            {
                if (_createdAtDateTime.HasValue) return _createdAtDateTime.Value;
                if (!DateTime.TryParse(CreatedAt, out DateTime result)) return null;
                return _createdAtDateTime = result;
            }
        }

        /// <summary>
        /// The type of event (e.g., "motion")
        /// </summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; }

        /// <summary>
        /// Indicates if the event was answered
        /// </summary>
        [JsonPropertyName("answered")]
        public bool Answered { get; set; }

        /// <summary>
        /// List of events related to this historical event
        /// </summary>
        [JsonPropertyName("events")]
        public List<object> Events { get; set; } = new List<object>();

        /// <summary>
        /// Indicates if the event is marked as favorite
        /// </summary>
        [JsonPropertyName("favorite")]
        public bool Favorite { get; set; }

        /// <summary>
        /// URL to the snapshot associated with this event
        /// </summary>
        [JsonPropertyName("snapshot_url")]
        public string SnapshotUrl { get; set; }

        /// <summary>
        /// Information about the recording status
        /// </summary>
        [JsonPropertyName("recording")]
        public RecordingStatus Recording { get; set; }

        /// <summary>
        /// Duration of the event in seconds
        /// </summary>
        [JsonPropertyName("duration")]
        public double Duration { get; set; }

        /// <summary>
        /// Computer Vision properties related to this event
        /// </summary>
        [JsonPropertyName("cv_properties")]
        public CvProperties CvProperties { get; set; }

        /// <summary>
        /// General properties related to this event
        /// </summary>
        [JsonPropertyName("properties")]
        public EventProperties Properties { get; set; }

        /// <summary>
        /// Information about the doorbot that captured this event
        /// </summary>
        [JsonPropertyName("doorbot")]
        public DoorbotInfo Doorbot { get; set; }

        /// <summary>
        /// Indicates if the event is encrypted end-to-end
        /// </summary>
        [JsonPropertyName("is_e2ee")]
        public bool IsE2Ee { get; set; }

        /// <summary>
        /// Indicates if there was a subscription at the time of the event
        /// </summary>
        [JsonPropertyName("had_subscription")]
        public bool HadSubscription { get; set; }

        /// <summary>
        /// Owner ID associated with this event
        /// </summary>
        [JsonPropertyName("owner_id")]
        public string OwnerId { get; set; }
    }

    public class RecordingStatus
    {
        [JsonPropertyName("status")]
        public string Status { get; set; }
    }

    public class CvProperties
    {
        [JsonPropertyName("person_detected")]
        public bool? PersonDetected { get; set; }

        [JsonPropertyName("stream_broken")]
        public bool? StreamBroken { get; set; }

        [JsonPropertyName("detection_type")]
        public string? DetectionType { get; set; }

        [JsonPropertyName("detection_types")]
        public List<DetectionType>? DetectionTypes { get; set; }

        [JsonPropertyName("security_alerts")]
        public object? SecurityAlerts { get; set; }
    }

    public class DetectionType
    {
        [JsonPropertyName("detection_type")]
        public string Type { get; set; }

        [JsonPropertyName("verified_timestamps")]
        public List<long> VerifiedTimestamps { get; set; }
    }

    public class EventProperties
    {
        [JsonPropertyName("is_alexa")]
        public bool IsAlexa { get; set; }

        [JsonPropertyName("is_sidewalk")]
        public bool IsSidewalk { get; set; }

        [JsonPropertyName("is_autoreply")]
        public bool IsAutoreply { get; set; }
    }

    public class DoorbotInfo
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }
    }
}
