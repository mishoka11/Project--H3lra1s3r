import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
    vus: 5,             // 5 virtual users
    duration: '10s',    // run for 10 seconds
    thresholds: {
        http_req_duration: ['p(95)<500'], // 95% requests < 500ms
        http_req_failed: ['rate<0.01'],   // <1% errors allowed
    },
};

export default function () {
    // ✅ Using k6’s public test site (always available)
    let res = http.get('https://test.k6.io');

    check(res, {
        'status is 200': (r) => r.status === 200,
    });

    sleep(1);
}
