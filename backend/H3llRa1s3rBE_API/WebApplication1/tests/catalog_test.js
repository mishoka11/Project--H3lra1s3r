import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
    vus: 10,          // virtual users
    duration: '30s',  // test length
    thresholds: {
        http_req_duration: ['p(95)<500'], // 95% of requests < 500ms
    },
};

export default function () {
    let res = http.get('http://localhost:8080/catalog');
    check(res, {
        'status is 200': (r) => r.status === 200,
    });
    sleep(1);
}
