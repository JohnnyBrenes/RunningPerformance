/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { CreateLifecycleRequest } from '../models/CreateLifecycleRequest';
import type { ExportJobResponse } from '../models/ExportJobResponse';
import type { LifecycleRequestResponse } from '../models/LifecycleRequestResponse';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class DataGovernanceService {
    /**
     * @returns ExportJobResponse OK
     * @throws ApiError
     */
    public static getExports(): CancelablePromise<Array<ExportJobResponse>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/exports',
        });
    }
    /**
     * @returns ExportJobResponse OK
     * @throws ApiError
     */
    public static createExport({
        idempotencyKey,
    }: {
        idempotencyKey?: string,
    }): CancelablePromise<ExportJobResponse> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/exports',
            headers: {
                'Idempotency-Key': idempotencyKey,
            },
            errors: {
                400: `Bad Request`,
                409: `Conflict`,
                413: `Payload Too Large`,
            },
        });
    }
    /**
     * @returns any OK
     * @throws ApiError
     */
    public static downloadExport({
        exportId,
    }: {
        exportId: string,
    }): CancelablePromise<any> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/exports/{exportId}/download',
            path: {
                'exportId': exportId,
            },
            errors: {
                404: `Not Found`,
                410: `Gone`,
            },
        });
    }
    /**
     * @returns LifecycleRequestResponse OK
     * @throws ApiError
     */
    public static getLifecycleRequests(): CancelablePromise<Array<LifecycleRequestResponse>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/lifecycle-requests',
        });
    }
    /**
     * @returns LifecycleRequestResponse Created
     * @throws ApiError
     */
    public static createLifecycleRequest({
        requestBody,
    }: {
        requestBody: CreateLifecycleRequest,
    }): CancelablePromise<LifecycleRequestResponse> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/lifecycle-requests',
            body: requestBody,
            mediaType: 'application/json',
            errors: {
                400: `Bad Request`,
                404: `Not Found`,
            },
        });
    }
}
