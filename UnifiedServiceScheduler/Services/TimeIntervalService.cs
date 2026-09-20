using UnifiedServiceScheduler.Data.Entities;

namespace UnifiedServiceScheduler.Services;

/// <summary>
/// Provides time interval operations for appointment scheduling.
/// Implements half-open interval semantics [StartTime, EndTime) for precise scheduling.
/// </summary>
public class TimeIntervalService
{
    /// <summary>
    /// Calculates the end time of an appointment based on service type duration.
    /// Uses the service type's predefined duration to determine when the appointment ends.
    /// </summary>
    /// <param name="startTime">The desired start time for the appointment</param>
    /// <param name="serviceType">The service type with predefined duration</param>
    /// <returns>The calculated end time for the appointment</returns>
    public DateTime CalculateAppointmentEndTime(DateTime startTime, ServiceType serviceType)
    {
        if (serviceType == null)
            throw new ArgumentNullException(nameof(serviceType));

        return startTime.Add(serviceType.Duration);
    }

    /// <summary>
    /// Determines if two time intervals overlap using half-open interval semantics [start, end).
    /// Appointments occupy [StartTime, EndTime), making resources available again at exactly EndTime.
    /// 
    /// Overlap occurs when: existing.StartTime < requested.EndTime && requested.StartTime < existing.EndTime
    /// </summary>
    /// <param name="existingStart">Start time of existing appointment</param>
    /// <param name="existingEnd">End time of existing appointment</param>
    /// <param name="requestedStart">Start time of requested appointment</param>
    /// <param name="requestedEnd">End time of requested appointment</param>
    /// <returns>True if the intervals overlap, false otherwise</returns>
    public bool DoIntervalsOverlap(DateTime existingStart, DateTime existingEnd, 
        DateTime requestedStart, DateTime requestedEnd)
    {
        // Half-open interval overlap detection: [existing.Start, existing.End) overlaps [requested.Start, requested.End)
        // Overlap occurs when: existing.StartTime < requested.EndTime && requested.StartTime < existing.EndTime
        return existingStart < requestedEnd && requestedStart < existingEnd;
    }
}