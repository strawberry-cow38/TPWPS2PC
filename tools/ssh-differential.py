#!/usr/bin/env python3
"""Compare the managed decoder with the historical FFmpeg implementation.

All archives, compiled historical code, RGBA snapshots, and reports are written to
an explicitly supplied NEW directory outside Git. Python only orchestrates builds;
the comparison executable has no external-decoder code or runtime fallback.
"""
import argparse
import io
import json
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import tarfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('pairs', type=Path)
parser.add_argument('work', type=Path)
parser.add_argument('--ffmpeg', type=Path, required=True)
parser.add_argument('--baseline', default='15730ca4f489873fa7029f0cb3c3a005bbc3aaca')
args = parser.parse_args()
repo = Path(__file__).resolve().parents[1]
work = args.work.resolve()
for parent in [work, *work.parents]:
    if (parent / '.git').exists():
        parser.error('the work directory must be outside every Git checkout')
work.mkdir(parents=True, exist_ok=False)
legacy = work / 'legacy'
legacy.mkdir()
archive = subprocess.check_output(['git', 'archive', args.baseline, 'core/TPW.PS2.Data', 'tools/TPW.PS2.SshScore'], cwd=repo)
with tarfile.open(fileobj=io.BytesIO(archive)) as tar:
    tar.extractall(legacy, filter='data')
shutil.copytree(repo / 'tools/TPW.PS2.SshDiff', legacy / 'tools/TPW.PS2.SshDiff',
                ignore=shutil.ignore_patterns('bin', 'obj'))
reference_env = os.environ | {'TPW_FFMPEG': str(args.ffmpeg.resolve())}
managed_env = os.environ | {'TPW_FFMPEG': '/nonexistent/ffmpeg'}
# Remove the supplied FFmpeg directory, independently of TPW_FFMPEG.
managed_env['PATH'] = os.pathsep.join(p for p in os.environ.get('PATH', '').split(os.pathsep)
                                     if Path(p).resolve() != args.ffmpeg.resolve().parent)
pairs = str(args.pairs.resolve())
snapshots = str(work / 'reference-rgba')

def run(name, command, cwd, env, expected):
    print(f"{name}: TPW_FFMPEG={shlex.quote(env['TPW_FFMPEG'])} PATH={shlex.quote(env['PATH'])} "
          + shlex.join(command), flush=True)
    with (work / (name + '.txt')).open('w') as output:
        result = subprocess.run(command, cwd=cwd, env=env, stdout=output, stderr=subprocess.STDOUT)
    lines = (work / (name + '.txt')).read_text().splitlines()
    print('\n'.join(lines[-8:]), flush=True)
    if result.returncode not in expected:
        raise SystemExit(f'{name}: unexpected exit {result.returncode}; see {work / (name + ".txt")}')

def dotnet(project, *arguments):
    return ['dotnet', 'run', '--project', 'tools/' + project, '-c', 'Release', '--', *arguments]

run('baseline-score', dotnet('TPW.PS2.SshScore', pairs, '--json', str(work / 'baseline-score.json')),
    legacy, reference_env, [0, 2])
run('capture', dotnet('TPW.PS2.SshDiff', 'capture', pairs, snapshots), legacy, reference_env, [0])
run('self-tests', dotnet('TPW.PS2.SshScore', '--self-test'), repo, managed_env, [0])
run('managed-diff', dotnet('TPW.PS2.SshDiff', 'compare', pairs, snapshots), repo, managed_env, [0])
run('perturb-diff', dotnet('TPW.PS2.SshDiff', 'compare', pairs, snapshots, '--perturb'), repo, managed_env, [2])
run('managed-score', dotnet('TPW.PS2.SshScore', pairs, '--json', str(work / 'managed-score.json')),
    repo, managed_env, [0, 2])
old = json.loads((work / 'baseline-score.json').read_text())['results']
new = json.loads((work / 'managed-score.json').read_text())['results']
if [r['File'] for r in old] != [r['File'] for r in new]:
    raise SystemExit('Scorer populations differ')
same = 0
additions = []
for before, after in zip(old, new):
    if before == after:
        same += 1
    elif (before['Error'] or '').startswith('SHPS type 0x02;') and after['Error'] is None:
        additions.append(after['File'])
    else:
        raise SystemExit(f'Unexpected scorer change: {before["File"]}\n{before}\n{after}')
print(f'Scorer records identical {same} of {len(old)} SSH files; added type 0x02 decodes {len(additions)} of {len(old)}.')
for file in additions:
    print('ADDED ' + file)
print('Reports and reference buffers: ' + str(work))
