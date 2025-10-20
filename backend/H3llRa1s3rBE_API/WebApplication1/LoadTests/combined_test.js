import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
    vus: 2, // virtual users
    duration: '30s',
};

export default function () {
    const urls = [
        'http://catalog-service:8080/healthz/live',
        'http://order-service:8080/healthz/live',
        'http://design-service:8080/healthz/live',
    ];

    for (const url of urls) {
        const res = http.get(url);
        check(res, {
            'status is 200': (r) => r.status === 200,
        });
        sleep(1);
    }
}
