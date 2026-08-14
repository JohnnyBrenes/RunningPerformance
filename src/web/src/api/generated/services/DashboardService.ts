/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { DashboardResponse } from '../models/DashboardResponse';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class DashboardService {
    /**
     * @returns DashboardResponse OK
     * @throws ApiError
     */
    public static getDashboard({
        weeks,
    }: {
        weeks: number | string,
    }): CancelablePromise<DashboardResponse> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/dashboard',
            query: {
                'weeks': weeks,
            },
            errors: {
                400: `Bad Request`,
            },
        });
    }
}
