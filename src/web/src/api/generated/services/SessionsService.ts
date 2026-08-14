/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ChangeSessionActivityLinkRequest } from '../models/ChangeSessionActivityLinkRequest';
import type { LinkSessionActivityRequest } from '../models/LinkSessionActivityRequest';
import type { SavePlannedSessionOutcomeRequest } from '../models/SavePlannedSessionOutcomeRequest';
import type { SaveSessionCheckinRequest } from '../models/SaveSessionCheckinRequest';
import type { SessionCompletionResponse } from '../models/SessionCompletionResponse';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class SessionsService {
    /**
     * @returns SessionCompletionResponse OK
     * @throws ApiError
     */
    public static getSessionCompletion({
        sessionId,
    }: {
        sessionId: string,
    }): CancelablePromise<SessionCompletionResponse> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/sessions/{sessionId}/completion',
            path: {
                'sessionId': sessionId,
            },
            errors: {
                404: `Not Found`,
            },
        });
    }
    /**
     * @returns SessionCompletionResponse Created
     * @throws ApiError
     */
    public static createAutomaticSessionLinkProposal({
        sessionId,
    }: {
        sessionId: string,
    }): CancelablePromise<SessionCompletionResponse> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/sessions/{sessionId}/links/proposals',
            path: {
                'sessionId': sessionId,
            },
            errors: {
                404: `Not Found`,
                409: `Conflict`,
            },
        });
    }
    /**
     * @returns SessionCompletionResponse Created
     * @throws ApiError
     */
    public static linkSessionActivity({
        sessionId,
        requestBody,
    }: {
        sessionId: string,
        requestBody: LinkSessionActivityRequest,
    }): CancelablePromise<SessionCompletionResponse> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/sessions/{sessionId}/links',
            path: {
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
    /**
     * @returns SessionCompletionResponse OK
     * @throws ApiError
     */
    public static changeSessionActivityLink({
        sessionId,
        linkId,
        requestBody,
    }: {
        sessionId: string,
        linkId: string,
        requestBody: ChangeSessionActivityLinkRequest,
    }): CancelablePromise<SessionCompletionResponse> {
        return __request(OpenAPI, {
            method: 'PUT',
            url: '/api/v1/sessions/{sessionId}/links/{linkId}',
            path: {
                'sessionId': sessionId,
                'linkId': linkId,
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
     * @returns SessionCompletionResponse OK
     * @throws ApiError
     */
    public static savePlannedSessionOutcome({
        sessionId,
        requestBody,
    }: {
        sessionId: string,
        requestBody: SavePlannedSessionOutcomeRequest,
    }): CancelablePromise<SessionCompletionResponse> {
        return __request(OpenAPI, {
            method: 'PUT',
            url: '/api/v1/sessions/{sessionId}/outcome',
            path: {
                'sessionId': sessionId,
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
     * @returns SessionCompletionResponse OK
     * @throws ApiError
     */
    public static saveSessionCheckin({
        sessionId,
        checkinWindow,
        requestBody,
    }: {
        sessionId: string,
        checkinWindow: string,
        requestBody: SaveSessionCheckinRequest,
    }): CancelablePromise<SessionCompletionResponse> {
        return __request(OpenAPI, {
            method: 'PUT',
            url: '/api/v1/sessions/{sessionId}/checkins/{checkinWindow}',
            path: {
                'sessionId': sessionId,
                'checkinWindow': checkinWindow,
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
