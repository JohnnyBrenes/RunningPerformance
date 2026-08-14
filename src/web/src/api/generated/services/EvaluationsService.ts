/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ConfirmWeeklyDecisionRequest } from '../models/ConfirmWeeklyDecisionRequest';
import type { CreateWeeklyEvaluationSnapshotRequest } from '../models/CreateWeeklyEvaluationSnapshotRequest';
import type { WeeklyEvaluationDetailResponse } from '../models/WeeklyEvaluationDetailResponse';
import type { WeeklyEvaluationSummaryResponse } from '../models/WeeklyEvaluationSummaryResponse';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class EvaluationsService {
    /**
     * @returns WeeklyEvaluationSummaryResponse OK
     * @throws ApiError
     */
    public static getWeeklyEvaluations(): CancelablePromise<Array<WeeklyEvaluationSummaryResponse>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/evaluations',
        });
    }
    /**
     * @returns WeeklyEvaluationDetailResponse OK
     * @throws ApiError
     */
    public static getWeeklyEvaluation({
        evaluationId,
    }: {
        evaluationId: string,
    }): CancelablePromise<WeeklyEvaluationDetailResponse> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/evaluations/{evaluationId}',
            path: {
                'evaluationId': evaluationId,
            },
            errors: {
                404: `Not Found`,
            },
        });
    }
    /**
     * @returns WeeklyEvaluationDetailResponse Created
     * @throws ApiError
     */
    public static createWeeklyEvaluationSnapshot({
        requestBody,
    }: {
        requestBody: CreateWeeklyEvaluationSnapshotRequest,
    }): CancelablePromise<WeeklyEvaluationDetailResponse> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/evaluations/snapshots',
            body: requestBody,
            mediaType: 'application/json',
            errors: {
                400: `Bad Request`,
                404: `Not Found`,
                409: `Conflict`,
            },
        });
    }
    /**
     * @returns WeeklyEvaluationDetailResponse Created
     * @throws ApiError
     */
    public static confirmWeeklyDecision({
        evaluationId,
        requestBody,
    }: {
        evaluationId: string,
        requestBody: ConfirmWeeklyDecisionRequest,
    }): CancelablePromise<WeeklyEvaluationDetailResponse> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/evaluations/{evaluationId}/decisions',
            path: {
                'evaluationId': evaluationId,
            },
            body: requestBody,
            mediaType: 'application/json',
            errors: {
                400: `Bad Request`,
                404: `Not Found`,
                409: `Conflict`,
            },
        });
    }
}
