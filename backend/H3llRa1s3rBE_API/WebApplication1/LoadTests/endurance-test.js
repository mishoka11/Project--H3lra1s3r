import http from 'k6/http';
import { sleep } from 'k6';

export const options = {
    vus: 25,
    duration: '1m',
};

export default function () {
    http.get('http://order-service:8080/healthz/live');
    sleep(0.5);
}
