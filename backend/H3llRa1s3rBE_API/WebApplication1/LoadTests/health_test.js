import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
    vus: 2,
    duration: '30s',
};

export default function () {
    const urls = [
        'http://aa52acadb1ab14532bafc2296357a742-1919469305.eu-central-1.elb.amazonaws.com:8080/healthz/live',
        'http://af32dd9bd8f1244bebe6a6a482d22600-952858228.eu-central-1.elb.amazonaws.com:8080/healthz/live',
    ];

    for (const url of urls) {
        const res = http.get(url);
        check(res, { 'status is 200': (r) => r.status === 200 });
        sleep(1);
    }
}
