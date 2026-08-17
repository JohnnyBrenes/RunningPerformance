/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ActivityDetailResponse } from '../models/ActivityDetailResponse';
import type { ActivityPageResponse } from '../models/ActivityPageResponse';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class ActivitiesService {
    /**
     * @returns ActivityPageResponse OK
     * @throws ApiError
     */
    public static getActivities({
        page,
        pageSize,
        activityType,
        category,
        modality,
        from,
        to,
        minDistanceM,
        maxDistanceM,
        sort,
        direction,
    }: {
        page: number | string,
        pageSize: number | string,
        activityType?: string,
        category?: string,
        modality?: string,
        from?: string,
        to?: string,
        minDistanceM?: number | string,
        maxDistanceM?: number | string,
        sort?: string,
        direction?: string,
    }): CancelablePromise<ActivityPageResponse> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/activities',
            query: {
                'activityType': activityType,
                'category': category,
                'modality': modality,
                'from': from,
                'to': to,
                'minDistanceM': minDistanceM,
                'maxDistanceM': maxDistanceM,
                'sort': sort,
                'direction': direction,
                'page': page,
                'pageSize': pageSize,
            },
            errors: {
                400: `Bad Request`,
            },
        });
    }
    /**
     * @returns ActivityDetailResponse OK
     * @throws ApiError
     */
    public static getActivity({
        id,
    }: {
        id: string,
    }): CancelablePromise<ActivityDetailResponse> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/activities/{id}',
            path: {
                'id': id,
            },
            errors: {
                404: `Not Found`,
            },
        });
    }
}
