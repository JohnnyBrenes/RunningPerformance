/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { RaceGoalResponse } from './RaceGoalResponse';
export type TargetRaceResponse = {
    id: string;
    name: string;
    raceDate: string;
    distanceM: number | string;
    location: string | null;
    priority: string;
    status: string;
    timezoneName: string | null;
    updatedAt: string;
    currentGoal: (null | RaceGoalResponse);
};

