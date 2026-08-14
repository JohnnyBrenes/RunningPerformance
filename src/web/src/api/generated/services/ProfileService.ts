/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AthleteProfileResponse } from '../models/AthleteProfileResponse';
import type { HealthContextResponse } from '../models/HealthContextResponse';
import type { SaveHealthContextRequest } from '../models/SaveHealthContextRequest';
import type { UpdateAthleteProfileRequest } from '../models/UpdateAthleteProfileRequest';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class ProfileService {
    /**
     * @returns AthleteProfileResponse OK
     * @throws ApiError
     */
    public static getProfile(): CancelablePromise<AthleteProfileResponse> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/profile',
            errors: {
                404: `Not Found`,
            },
        });
    }
    /**
     * @returns AthleteProfileResponse OK
     * @throws ApiError
     */
    public static updateProfile({
        requestBody,
    }: {
        requestBody: UpdateAthleteProfileRequest,
    }): CancelablePromise<AthleteProfileResponse> {
        return __request(OpenAPI, {
            method: 'PUT',
            url: '/api/v1/profile',
            body: requestBody,
            mediaType: 'application/json',
            errors: {
                400: `Bad Request`,
            },
        });
    }
    /**
     * @returns HealthContextResponse OK
     * @throws ApiError
     */
    public static getHealthContexts(): CancelablePromise<Array<HealthContextResponse>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/health-contexts',
        });
    }
    /**
     * @returns HealthContextResponse Created
     * @throws ApiError
     */
    public static createHealthContext({
        requestBody,
    }: {
        requestBody: SaveHealthContextRequest,
    }): CancelablePromise<HealthContextResponse> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/health-contexts',
            body: requestBody,
            mediaType: 'application/json',
            errors: {
                400: `Bad Request`,
            },
        });
    }
    /**
     * @returns HealthContextResponse OK
     * @throws ApiError
     */
    public static updateHealthContext({
        id,
        requestBody,
    }: {
        id: string,
        requestBody: SaveHealthContextRequest,
    }): CancelablePromise<HealthContextResponse> {
        return __request(OpenAPI, {
            method: 'PUT',
            url: '/api/v1/health-contexts/{id}',
            path: {
                'id': id,
            },
            body: requestBody,
            mediaType: 'application/json',
            errors: {
                400: `Bad Request`,
                404: `Not Found`,
            },
        });
    }
}
