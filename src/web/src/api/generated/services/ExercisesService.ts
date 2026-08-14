/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ExerciseResponse } from '../models/ExerciseResponse';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class ExercisesService {
    /**
     * @returns ExerciseResponse OK
     * @throws ApiError
     */
    public static getExercises(): CancelablePromise<Array<ExerciseResponse>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/exercises',
        });
    }
    /**
     * @returns ExerciseResponse OK
     * @throws ApiError
     */
    public static getExercise({
        id,
    }: {
        id: string,
    }): CancelablePromise<ExerciseResponse> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/exercises/{id}',
            path: {
                'id': id,
            },
            errors: {
                404: `Not Found`,
            },
        });
    }
}
