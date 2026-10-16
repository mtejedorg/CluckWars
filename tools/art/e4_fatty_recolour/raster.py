import numpy as np, sys
from PIL import Image
src = sys.argv[1]; W = 1024
lines = open(src).read().splitlines()
bones = lines[0].split(' ',1)[1].split(',')
V=[];T=[]
for l in lines[2:]:
    if l[0]=='V': V.append([float(x) for x in l[2:].split()])
    elif l[0]=='T': T.append([int(x) for x in l[2:].split()])
V=np.array(V); T=np.array(T)
P=V[:,0:3]; N=V[:,3:6]; UV=V[:,6:8]; B=V[:,8].astype(int)
# forward = from body centre to beak verts (mesh space)
bi = bones.index('Beak'); fwd = P[B==bi].mean(0)-P.mean(0); fwd[2]=0; fwd/=np.linalg.norm(fwd)
print('bones',bones,'fwd',fwd, 'zrange',P[:,2].min(),P[:,2].max())
lab = np.full((W,W),-1,np.int16); nrm=np.zeros((W,W,3),np.float32); pos=np.zeros((W,W,3),np.float32)
for t in T:
    uv = UV[t]*[W,W]; uv[:,1]=W-uv[:,1]  # image row 0 = top = v 1
    x0,y0=np.floor(uv.min(0)).astype(int); x1,y1=np.ceil(uv.max(0)).astype(int)
    x0=max(x0,0);y0=max(y0,0);x1=min(x1,W-1);y1=min(y1,W-1)
    if x1<x0 or y1<y0: continue
    xs,ys=np.meshgrid(np.arange(x0,x1+1)+.5,np.arange(y0,y1+1)+.5)
    a,b,c=uv; d=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
    if abs(d)<1e-12: continue
    w0=((b[1]-c[1])*(xs-c[0])+(c[0]-b[0])*(ys-c[1]))/d; w1=((c[1]-a[1])*(xs-c[0])+(a[0]-c[0])*(ys-c[1]))/d; w2=1-w0-w1
    m=(w0>=-0.02)&(w1>=-0.02)&(w2>=-0.02)
    if not m.any(): continue
    yy=(ys[m]-.5).astype(int); xx=(xs[m]-.5).astype(int)
    ww=np.stack([w0[m],w1[m],w2[m]],1)
    # dominant bone by weight majority of corners
    bb=B[t]; best=bb[np.argmax(ww,1)]
    lab[yy,xx]=best; nrm[yy,xx]=ww@N[t]; pos[yy,xx]=ww@P[t]
np.savez_compressed(sys.argv[2], lab=lab, nrm=nrm, pos=pos, fwd=fwd, bones=np.array(bones))
cov=(lab>=0).mean(); print('coverage',cov)
pal=np.random.RandomState(3).randint(40,255,(len(bones),3))
img=np.zeros((W,W,3),np.uint8); img[lab>=0]=pal[lab[lab>=0]]
Image.fromarray(img).resize((512,512)).save(sys.argv[2].replace('.npz','_bones.png'))
for i,b in enumerate(bones): print(i,b,(lab==i).sum())
