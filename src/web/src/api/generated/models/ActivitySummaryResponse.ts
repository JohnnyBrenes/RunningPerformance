/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type ActivitySummaryResponse = {
    id: string;
    provisionalActivityKey: string | null;
    garminActivityId: number | string | null;
    activityType: string;
    activityCategory: string | null;
    modality: string | null;
    startedAtLocal: string;
    title: string | null;
    distanceM: number | string | null;
    durationSeconds: number | string | null;
    averagePaceSecondsPerKm: number | string | null;
    averageHeartRateBpm: number | string | null;
    maxHeartRateBpm: number | string | null;
    validationStatus: string;
};

