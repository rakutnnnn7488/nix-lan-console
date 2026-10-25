const W=480,H=270;
const P={bg:'#17152d',dark:'#242044',edge:'#45318a',pink:'#f6b2dc',hot:'#ff61bc',cyan:'#67dce4',white:'#fbeafa',muted:'#aa95cc'};
const clamp=(v,a=0,b=1)=>Math.max(a,Math.min(b,v));
const box=(g,x,y,w,h,c)=>{g.fillStyle=c;g.fillRect(x|0,y|0,w|0,h|0)};
function chrome(g,x,y,w,h,title){box(g,x,y,w,h,P.edge);box(g,x+2,y+2,w-4,h-4,P.white);box(g,x+3,y+3,w-6,16,P.pink);box(g,x+4,y+19,w-8,2,P.edge);g.fillStyle=P.edge;g.font='bold 10px monospace';g.fillText(title,x+9,y+15);for(let i=0;i<3;i++){box(g,x+w-57+i*17,y+5,13,12,P.edge);box(g,x+w-55+i*17,y+7,9,8,P.white)}g.fillText('×',x+w-20,y+15)}
function star(g,x,y,c){box(g,x-2,y,5,1,c);box(g,x,y-2,1,5,c)}
function heart(g,x,y,c){['0110110','1111111','1111111','0111110','0011100','0001000'].forEach((row,j)=>Array.from(row).forEach((bit,i)=>{if(bit==='1')box(g,x+i*2,y+j*2,2,2,c)}))}
function shorten(g,s,w){s=String(s||'');if(g.measureText(s).width<=w)return s;while(s&&g.measureText(s+'…').width>w)s=s.slice(0,-1);return s+'…'}
function wrap(g,s,w){const rows=[''];for(const ch of Array.from(s||'')){let i=rows.length-1;if(g.measureText(rows[i]+ch).width>w&&rows[i]){if(rows.length===2){rows[i]=shorten(g,rows[i]+'…',w);break}rows.push('');i++}rows[i]+=ch}return rows}
function fraction(line,time){if(!line)return 0;const words=Array.isArray(line.words)?line.words:[];if(!words.length)return clamp((time-line.startTime)/Math.max(.1,line.endTime-line.startTime));let n=0,total=0;for(const word of words){const len=Array.from(word.text||'').length;total+=len;n+=len*clamp((time-word.startTime)/Math.max(.01,word.endTime-word.startTime))}return total?clamp(n/total):0}
function mount(container,ctx){
 const canvas=document.createElement('canvas');canvas.width=W;canvas.height=H;canvas.setAttribute('aria-label','霓虹像素日记歌词动效');canvas.style.cssText='position:absolute;inset:0;width:100%;height:100%;object-fit:contain;image-rendering:pixelated;background:#17152d';container.appendChild(canvas);
 const g=canvas.getContext('2d',{alpha:false});g.imageSmoothingEnabled=false;let raf=0,dead=false,last=0;
 const song=ctx.song||{},title=String(song.name||song.title||'NOW PLAYING'),artist=String(song.artist||'PIXEL RADIO');
 const particles=Array.from({length:28},(_,i)=>({x:(i*167+31)%W,y:(i*83+49)%H,s:i%4?1:2}));
 function paint(now){if(dead)return;raf=requestAnimationFrame(paint);if(now-last<33)return;last=now;
  const time=ctx.lyricTime?.get?.()??0,index=ctx.staticMode&&ctx.staticLineIndex!=null?ctx.staticLineIndex:(ctx.getLineIndex?.()??-1),line=ctx.lines?.[index],next=ctx.lines?.[index+1],power=clamp(ctx.audio?.getPower?.()??0),progress=fraction(line,time);
  box(g,0,0,W,H,P.dark);for(let y=0;y<H;y+=18)for(let x=0;x<W;x+=18)box(g,x+((y/18)%2)*9,y,2,2,'#57477d');
  particles.forEach((p,i)=>box(g,p.x,(p.y+now*.004*(i%3+1))%H,p.s,p.s,i%3?P.muted:P.cyan));
  for(let x=0;x<W;x+=20){box(g,x,H-27,1,27,'#554276');box(g,0,H-27+(x%60)/3,W,1,'#4b376e')}
  chrome(g,15,12,450,246,'NEON DIARY / LYRIC.exe');box(g,20,33,440,220,P.bg);
  for(let i=0;i<12;i++)star(g,30+i*39,52+(i*17)%106,i%2?P.pink:P.cyan);
  box(g,32,47,416,22,'#30285a');box(g,35,50,410,16,'#49347c');box(g,41,54,7,7,Math.sin(now*.006)>0?P.hot:P.pink);g.font='bold 10px monospace';g.fillStyle=P.white;g.fillText('ON AIR // PIXEL RADIO',54,62);g.textAlign='right';g.fillStyle=P.cyan;g.fillText(String(Math.floor(time/60)).padStart(2,'0')+':'+String(Math.floor(time%60)).padStart(2,'0'),435,62);g.textAlign='left';
  chrome(g,37,76,406,121,'♪  '+shorten(g,title,245));box(g,42,97,396,94,'#221d42');for(let i=0;i<8;i++){box(g,46+i*52,99,1,89,'#373056');box(g,43,99+i*13,393,1,'#342b52')}
  heart(g,54,107,P.hot);star(g,406,118,P.cyan);star(g,389,164,P.hot);
  g.font='bold 20px "Microsoft YaHei UI","Yu Gothic",sans-serif';g.textAlign='left';const lyric=line?.fullText||line?.text||'等待歌词接入…',rows=wrap(g,lyric,338),base=rows.length===1?148:132;
  rows.forEach((row,j)=>{const y=base+j*27,x=240-g.measureText(row).width/2;g.fillStyle='#574778';g.fillText(row,x+2,y+2);g.fillStyle='#a996c7';g.fillText(row,x,y);const prior=Array.from(rows.slice(0,j).join('')).length,count=Array.from(row).length,local=clamp((progress*Array.from(lyric).length-prior)/Math.max(1,count));g.save();g.beginPath();g.rect(x-2,y-24,Math.max(0,g.measureText(row).width*local+3),30);g.clip();g.fillStyle=P.white;g.shadowColor=P.hot;g.shadowBlur=7+Math.round(power*6);g.fillText(row,x,y);g.restore()});
  g.textAlign='left';g.font='bold 9px "Microsoft YaHei UI",sans-serif';g.fillStyle=P.muted;g.fillText(shorten(g,next?.fullText||next?.text||'♪',340),56,181);box(g,46,185,388,3,'#554477');box(g,46,185,388*progress,3,P.hot);
  chrome(g,37,203,274,38,'MESSAGE');box(g,42,223,264,13,'#dfd1ed');g.fillStyle=P.edge;g.fillText('☆  霓虹正在传递你的歌声',49,233);
  chrome(g,317,203,126,38,'LEVEL');for(let i=0;i<15;i++){const h=3+Math.round((.2+.8*Math.abs(Math.sin(now*.003+i*.78)))*(7+power*5));box(g,326+i*7,236-h,4,h,i%3===0?P.hot:P.cyan)}
  box(g,15,257,450,3,P.cyan);if(Math.floor(now/90)%2===0)for(let y=40;y<252;y+=4)box(g,20,y,440,1,'rgba(255,210,255,.035)');g.fillStyle=P.white;g.font='8px monospace';g.textAlign='right';g.fillText(artist.slice(0,24),440,246);g.textAlign='left';
 }
 raf=requestAnimationFrame(paint);return()=>{dead=true;cancelAnimationFrame(raf);canvas.remove()};
}
export default function activate(folium){folium.registries.visualizers.register({id:'neon-pixel-diary',label:{'zh-CN':'霓虹像素日记',en:'Neon Pixel Diary'},order:430,hostLayers:{background:false,subtitles:false},mount})}


