/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { CloneTrainingPlanDraftRequest } from '../models/CloneTrainingPlanDraftRequest';
import type { TrainingPlanDetailResponse } from '../models/TrainingPlanDetailResponse';
import type { TrainingPlanSummaryResponse } from '../models/TrainingPlanSummaryResponse';
import type { UpdatePlannedSessionRequest } from '../models/UpdatePlannedSessionRequest';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class PlansService {
    /**
     * @returns TrainingPlanSummaryResponse OK
     * @throws ApiError
     */
    public static getTrainingPlans(): CancelablePromise<Array<TrainingPlanSummaryResponse>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/plans',
        });
    }
    /**
     * @returns TrainingPlanDetailResponse OK
     * @throws ApiError
     */
    public static getCurrentTrainingPlan(): CancelablePromise<TrainingPlanDetailResponse> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/plans/current',
            errors: {
                404: `Not Found`,
            },
        });
    }
    /**
     * @returns TrainingPlanDetailResponse OK
     * @throws ApiError
     */
    public static getTrainingPlanVersion({
        planId,
        versionId,
    }: {
        planId: string,
        versionId: string,
    }): CancelablePromise<TrainingPlanDetailResponse> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/plans/{planId}/versions/{versionId}',
            path: {
                'planId': planId,
                'versionId': versionId,
            },
            errors: {
                404: `Not Found`,
            },
        });
    }
    /**
     * @returns TrainingPlanDetailResponse Created
     * @throws ApiError
     */
    public static cloneTrainingPlanDraft({
        planId,
        requestBody,
    }: {
        planId: string,
        requestBody: CloneTrainingPlanDraftRequest,
    }): CancelablePromise<TrainingPlanDetailResponse> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/plans/{planId}/drafts',
            path: {
                'planId': planId,
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
    /**
     * @returns TrainingPlanDetailResponse OK
     * @throws ApiError
     */
    public static publishTrainingPlanVersion({
        planId,
        versionId,
    }: {
        planId: string,
        versionId: string,
    }): CancelablePromise<TrainingPlanDetailResponse> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/plans/{planId}/versions/{versionId}/publish',
            path: {
                'planId': planId,
                'versionId': versionId,
            },
            errors: {
                404: `Not Found`,
                409: `Conflict`,
            },
        });
    }
    /**
     * @returns TrainingPlanDetailResponse OK
     * @throws ApiError
     */
    public static updatePlannedSession({
        planId,
        versionId,
        sessionId,
        requestBody,
    }: {
        planId: string,
        versionId: string,
        sessionId: string,
        requestBody: UpdatePlannedSessionRequest,
    }): CancelablePromise<TrainingPlanDetailResponse> {
        return __request(OpenAPI, {
            method: 'PUT',
            url: '/api/v1/plans/{planId}/versions/{versionId}/sessions/{sessionId}',
            path: {
                'planId': planId,
                'versionId': versionId,
                'sessionId': sessionId,
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
