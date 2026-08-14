/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { IngestionItemErrorResponse } from './IngestionItemErrorResponse';
export type IngestionRunResponse = {
    id: string;
    runType: string;
    status: string;
    toolVersion: string;
    schemaVersion: string;
    correlationId: string;
    startedAt: string | null;
    finishedAt: string | null;
    itemCount: number | string;
    successCount: number | string;
    failureCount: number | string;
    attemptCount: number | string;
    heartbeatAt: string | null;
    createdAt: string;
    sourceFileId: string | null;
    originalName: string | null;
    sha256: string | null;
    sizeBytes: number | string | null;
    errors: Array<IngestionItemErrorResponse>;
};

