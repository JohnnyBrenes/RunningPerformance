/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type ActivityPlannedContextResponse = {
    plannedSessionId: string;
    linkId: string;
    linkStatus: string;
    linkMethod: string;
    linkConfidence: number | string | null;
    scheduledDate: string;
    sessionType: string;
    modality: string | null;
    obligation: string;
    objective: string;
    plannedDistanceM: number | string | null;
    plannedDurationSeconds: number | string | null;
    targetRpeMin: number | string | null;
    targetRpeMax: number | string | null;
    terrain: string | null;
    planVersionStatus: string;
    executionStatus: string | null;
    logicalActivityCount: number | string;
    logicalDistanceM: number | string | null;
    logicalDurationSeconds: number | string | null;
    sessionRpe: number | string | null;
    srpeLoad: number | string | null;
};

