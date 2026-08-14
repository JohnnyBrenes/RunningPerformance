/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { CreateRaceGoalRequest } from '../models/CreateRaceGoalRequest';
import type { RaceGoalResponse } from '../models/RaceGoalResponse';
import type { SaveTargetRaceRequest } from '../models/SaveTargetRaceRequest';
import type { TargetRaceResponse } from '../models/TargetRaceResponse';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class RacesService {
    /**
     * @returns TargetRaceResponse OK
     * @throws ApiError
     */
    public static getRaces(): CancelablePromise<Array<TargetRaceResponse>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/races',
        });
    }
    /**
     * @returns TargetRaceResponse Created
     * @throws ApiError
     */
    public static createRace({
        requestBody,
    }: {
        requestBody: SaveTargetRaceRequest,
    }): CancelablePromise<TargetRaceResponse> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/races',
            body: requestBody,
            mediaType: 'application/json',
            errors: {
                400: `Bad Request`,
            },
        });
    }
    /**
     * @returns TargetRaceResponse OK
     * @throws ApiError
     */
    public static updateRace({
        id,
        requestBody,
    }: {
        id: string,
        requestBody: SaveTargetRaceRequest,
    }): CancelablePromise<TargetRaceResponse> {
        return __request(OpenAPI, {
            method: 'PUT',
            url: '/api/v1/races/{id}',
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
    /**
     * @returns RaceGoalResponse OK
     * @throws ApiError
     */
    public static getRaceGoals({
        id,
    }: {
        id: string,
    }): CancelablePromise<Array<RaceGoalResponse>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/races/{id}/goals',
            path: {
                'id': id,
            },
            errors: {
                404: `Not Found`,
            },
        });
    }
    /**
     * @returns RaceGoalResponse Created
     * @throws ApiError
     */
    public static createRaceGoal({
        id,
        requestBody,
    }: {
        id: string,
        requestBody: CreateRaceGoalRequest,
    }): CancelablePromise<RaceGoalResponse> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/races/{id}/goals',
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
