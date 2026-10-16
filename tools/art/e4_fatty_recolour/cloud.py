import numpy as np, sys; from PIL import Image
d=np.load(sys.argv[1]); lab=d['lab']; pos=d['pos']; nrm=d['nrm']
alb=np.asarray(Image.open(sys.argv[2]).convert('RGB'))
m=lab>=0; P=pos[m]; C=alb[m]; Nn=nrm[m]
S=400
def view(ax_h, ax_v, depth, sign, name):
    img=np.full((S,S,3),40,np.uint8); zb=np.full((S,S),-1e9)
    h=P[:,ax_h]; v=P[:,ax_v]; dp=P[:,depth]*sign
    xs=((h+0.9)/1.8*S).astype(int); ys=(S-1-(v+0.1)/1.8*S).astype(int)
    ok=(xs>=0)&(xs<S)&(ys>=0)&(ys<S)
    for x,y,dd,c in zip(xs[ok],ys[ok],dp[ok],C[ok]):
        if dd>zb[y,x]: zb[y,x]=dd; img[y,x]=c
    return img
# mesh: z up, forward -y.  front view: look from -y => horizontal x, depth = -y
f=view(0,2,1,-1,'front'); s=view(1,2,0,1,'side'); b=view(0,2,1,1,'back')
t=np.concatenate([f,s,b],1)
# grid lines every 0.2
for k in range(0,10):
    yy=int(S-1-(k*0.2+0.1)/1.8*S)
    if 0<=yy<S: t[yy,::4]=[0,0,255]
Image.fromarray(t).resize((S*3*2,S*2),Image.NEAREST).save(sys.argv[3])
