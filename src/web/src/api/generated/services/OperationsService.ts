/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { FreeTierUsageReportResponse } from '../models/FreeTierUsageReportResponse';
import type { RecordFreeTierUsageRequest } from '../models/RecordFreeTierUsageRequest';
import type { ServiceStatus } from '../models/ServiceStatus';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class OperationsService {
    /**
     * @returns ServiceStatus OK
     * @throws ApiError
     */
    public static getServiceStatus(): CancelablePromise<ServiceStatus> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/status',
        });
    }
    /**
     * @returns FreeTierUsageReportResponse Created
     * @throws ApiError
     */
    public static recordFreeTierQuotaUsage({
        requestBody,
    }: {
        requestBody: RecordFreeTierUsageRequest,
    }): CancelablePromise<FreeTierUsageReportResponse> {
        return __request(OpenAPI, {
            method: 'PUT',
            url: '/api/v1/operations/quota-usage',
            body: requestBody,
            mediaType: 'application/json',
            errors: {
                400: `Bad Request`,
            },
        });
    }
}
