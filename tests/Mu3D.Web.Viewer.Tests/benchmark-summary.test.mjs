import {test} from 'node:test';
import assert from 'node:assert/strict';
import {summarize} from '../Mu3D.Web.Validation/wwwroot/benchmark-summary.mjs';

test('nearest-rank percentiles retain outliers without mutating samples', () => {
  const samples = [100, ...Array.from({length:19}, (_,i) => i+1)];
  const result = summarize(samples);
  assert.deepEqual(result, {count:20,mean:14.5,median:10,p95:19,min:1,max:100});
  assert.equal(samples[0],100);
});
test('empty measurements remain unavailable; a zero duration is valid', () => {
  assert.equal(summarize([]),null);
  assert.deepEqual(summarize([0]), {count:1,mean:0,median:0,p95:0,min:0,max:0});
});
test('corrupt timings are rejected', () => {
  for (const value of [-1,NaN,Infinity]) assert.throws(() => summarize([value]),TypeError);
});
