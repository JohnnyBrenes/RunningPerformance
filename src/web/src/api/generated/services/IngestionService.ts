/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { CsvImportAcceptedResponse } from '../models/CsvImportAcceptedResponse';
import type { FitImportAcceptedResponse } from '../models/FitImportAcceptedResponse';
import type { IngestionRunResponse } from '../models/IngestionRunResponse';
import type { Stream } from '../models/Stream';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class IngestionService {
    /**
     * @returns FitImportAcceptedResponse Accepted
     * @throws ApiError
     */
    public static enqueueFit({
        requestBody,
        fileName,
        garminActivityId,
    }: {
        requestBody: Stream,
        fileName?: string,
        garminActivityId?: number | string,
    }): CancelablePromise<FitImportAcceptedResponse> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/ingestion-runs/fit',
            query: {
                'fileName': fileName,
                'garminActivityId': garminActivityId,
            },
            body: requestBody,
            mediaType: 'application/vnd.ant.fit',
            errors: {
                400: `Bad Request`,
                413: `Payload Too Large`,
                507: `Insufficient Storage`,
            },
        });
    }
    /**
     * @returns FitImportAcceptedResponse Accepted
     * @throws ApiError
     */
    public static reprocessFit({
        id,
    }: {
        id: string,
    }): CancelablePromise<FitImportAcceptedResponse> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/ingestion-runs/{id}/reprocess',
            path: {
                'id': id,
            },
            errors: {
                404: `Not Found`,
            },
        });
    }
    /**
     * @returns CsvImportAcceptedResponse Accepted
     * @throws ApiError
     */
    public static enqueueHistoricalCsv({
        requestBody,
        fileName,
    }: {
        requestBody: Stream,
        fileName?: string,
    }): CancelablePromise<CsvImportAcceptedResponse> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/ingestion-runs/historical-csv',
            query: {
                'fileName': fileName,
            },
            body: requestBody,
            mediaType: 'text/csv',
            errors: {
                400: `Bad Request`,
                413: `Payload Too Large`,
                507: `Insufficient Storage`,
            },
        });
    }
    /**
     * @returns IngestionRunResponse OK
     * @throws ApiError
     */
    public static getIngestionRun({
        id,
    }: {
        id: string,
    }): CancelablePromise<IngestionRunResponse> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/ingestion-runs/{id}',
            path: {
                'id': id,
            },
            errors: {
                404: `Not Found`,
            },
        });
    }
}
