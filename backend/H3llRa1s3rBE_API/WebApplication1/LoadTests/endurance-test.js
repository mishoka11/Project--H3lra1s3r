import http from 'k6/http';
import { sleep } from 'k6';

export const options = {
    vus: 25,
    duration: '5m',
};

export default function () {
    http.get('http://localhost:8082/api/v1/orders');
    sleep(0.5);
}
